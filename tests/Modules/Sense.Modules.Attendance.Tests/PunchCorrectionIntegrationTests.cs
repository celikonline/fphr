using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Corrections;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Corrections;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// FG-004 on real PostgreSQL: the AddPunchCorrectionRequests migration (partial unique index included), and Workflow's
/// ApprovalCompleted/ApprovalRejected delivered through IEventBus the way Sense.Worker's OutboxProcessor delivers them -
/// an approved correction updates the day, recomputes it and clears its missing punch anomaly.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PunchCorrectionIntegrationTests : TestFixture
{
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateTime NowUtc = new(2026, 9, 28, 7, 0, 0, DateTimeKind.Utc);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly FixedLocalClock _localClock = new(NowUtc);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceServices(configuration);
        services.AddModuleHandlers(
            AttendanceDbContext.SchemaName,
            typeof(AttendanceErrors).Assembly,
            typeof(AttendancePermissions).Assembly,
            typeof(AttendanceDbContext).Assembly,
            typeof(IWorkScheduleRepository).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();

    [Fact]
    public void WorkerRegistration_ResolvesThePunchCorrectionDecisionHandlers()
    {
        var configuration = ServiceProvider!.GetRequiredService<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        services.AddAttendanceIntegrationEventHandlers();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<ApprovalCompleted>>()
            .ShouldHaveSingleItem().ShouldBeOfType<PunchCorrectionApprovalCompletedHandler>();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<ApprovalRejected>>()
            .ShouldHaveSingleItem().ShouldBeOfType<PunchCorrectionApprovalRejectedHandler>();
    }

    [Fact]
    public async Task ApprovedCorrection_FillsTheMissingCheckOut_RecomputesTheDay_AndClearsItsAnomalies()
    {
        var (tenantId, employeeId, approvalId) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var planned = await PlannedDayAsync(employeeId);
        await RecordAsync(employeeId, planned.Start, null);
        (await FindDayAsync(employeeId)).Anomalies.ShouldBe(AttendanceAnomaly.MissingCheckOut);

        var submitted = await SubmitAsync(new SubmitMyPunchCorrectionCommand(Monday, null, planned.End, "Kart okutmayı unuttum"));
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        // The inbox summary the approver sees (Workflow.Contracts.IApprovalRequestSummaryProvider).
        using (var read = ServiceProvider!.CreateScope())
        {
            var provider = read.ServiceProvider.GetServices<IApprovalRequestSummaryProvider>()
                .Single(p => p.RequestType == ApprovalRequestTypes.PunchCorrection);
            var summary = await provider.GetSummaryAsync(tenantId, submitted.Value.Id, TestContext.Current.CancellationToken);
            summary.ShouldNotBeNull();
            summary.RequestDate.ShouldBe(Monday);
            summary.PunchCorrection.ShouldBe(new PunchCorrectionApprovalSummary(Monday, null, planned.End, planned.Start, null, "Kart okutmayı unuttum"));
        }

        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.PunchCorrection, submitted.Value.Id, employeeId), TestContext.Current.CancellationToken);

        var day = await FindDayAsync(employeeId);
        day.CheckIn.ShouldBe(planned.Start);
        day.CheckOut.ShouldBe(planned.End);
        day.Source.ShouldBe(AttendanceDaySource.Manual);
        day.WorkedMinutes.ShouldBe(day.PlannedMinutes);
        day.WorkedMinutes.ShouldBeGreaterThan(0);
        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);

        var request = await FindRequestAsync(submitted.Value.Id);
        request.Status.ShouldBe(PunchCorrectionStatus.Approved);
        request.DecidedAt.ShouldNotBeNull();

        // A redelivered event is a no-op (the request is no longer Pending).
        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.PunchCorrection, submitted.Value.Id, employeeId), TestContext.Current.CancellationToken);
        (await FindRequestAsync(submitted.Value.Id)).Status.ShouldBe(PunchCorrectionStatus.Approved);
    }

    [Fact]
    public async Task ApprovalAfterTheMonthGotLocked_MarksApplyFailed_AndLeavesTheDayUntouched()
    {
        var (tenantId, employeeId, approvalId) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var planned = await PlannedDayAsync(employeeId);
        await RecordAsync(employeeId, planned.Start, null);
        var submitted = await SubmitAsync(new SubmitMyPunchCorrectionCommand(Monday, null, planned.End, "Kart okutmayı unuttum"));
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            db.ShiftPlanLocks.Add(ShiftPlanLock.Lock(tenantId, Monday, NowUtc, null, ShiftPlanLockSource.Manual));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ServiceProvider!.GetRequiredService<IEventBus>()
            .Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.PunchCorrection, submitted.Value.Id, employeeId), TestContext.Current.CancellationToken);

        (await FindRequestAsync(submitted.Value.Id)).Status.ShouldBe(PunchCorrectionStatus.ApplyFailed);
        var day = await FindDayAsync(employeeId);
        day.CheckOut.ShouldBeNull();
        day.Anomalies.ShouldBe(AttendanceAnomaly.MissingCheckOut);

        // And a new request for the locked month is refused at submit too.
        var again = await SubmitAsync(new SubmitMyPunchCorrectionCommand(Monday, null, planned.End, "tekrar"));
        again.Error.Code.ShouldBe(AttendanceErrors.CorrectionPeriodLocked);
    }

    [Fact]
    public async Task RejectedCorrection_KeepsTheDay_AndFreesTheDayForANewRequest()
    {
        var (tenantId, employeeId, approvalId) = await ArrangeEmployeeAsync();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var planned = await PlannedDayAsync(employeeId);
        await RecordAsync(employeeId, planned.Start, null);
        var submitted = await SubmitAsync(new SubmitMyPunchCorrectionCommand(Monday, null, planned.End, "Kart okutmayı unuttum"));
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
        (await SubmitAsync(new SubmitMyPunchCorrectionCommand(Monday, null, planned.End, "ikinci")))
            .Error.Code.ShouldBe(AttendanceErrors.CorrectionPendingExists);

        await ServiceProvider!.GetRequiredService<IEventBus>()
            .Publish(new ApprovalRejected(tenantId, approvalId, ApprovalRequestTypes.PunchCorrection, submitted.Value.Id, employeeId, 1), TestContext.Current.CancellationToken);

        (await FindRequestAsync(submitted.Value.Id)).Status.ShouldBe(PunchCorrectionStatus.Rejected);
        (await FindDayAsync(employeeId)).CheckOut.ShouldBeNull();
        (await SubmitAsync(new SubmitMyPunchCorrectionCommand(Monday, null, planned.End, "yeniden"))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task PartialUniqueIndex_AllowsOnlyOnePendingRequestPerDay_ButKeepsClosedOnesAsHistory()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            var withdrawn = NewRequest(tenantId, employeeId);
            withdrawn.Withdraw(NowUtc).IsSuccess.ShouldBeTrue();
            db.PunchCorrectionRequests.AddRange(withdrawn, NewRequest(tenantId, employeeId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var duplicate = ServiceProvider.CreateScope();
        var context = duplicate.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        context.PunchCorrectionRequests.Add(NewRequest(tenantId, employeeId));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static PunchCorrectionRequest NewRequest(Guid tenantId, Guid employeeId) =>
        PunchCorrectionRequest.Submit(tenantId, employeeId, Monday, new TimeOnly(9, 0), null, "gerekçe", null, null, NowUtc).Value;

    /// <summary>EmployeeHired assigns the tenant's default schedule (so Monday is a planned working day); the caller is that employee.</summary>
    private async Task<(Guid TenantId, Guid EmployeeId, Guid ApprovalId)> ArrangeEmployeeAsync()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var hireDate = new DateOnly(2026, 1, 1);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, hireDate, hireDate), TestContext.Current.CancellationToken);
        _currentUser.EmployeeId.Returns(employeeId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _workflow.StartAsync(
                ApprovalRequestTypes.PunchCorrection, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        return (tenantId, employeeId, approvalId);
    }

    private async Task<(TimeOnly Start, TimeOnly End)> PlannedDayAsync(Guid employeeId)
    {
        using var scope = ServiceProvider!.CreateScope();
        var day = await scope.ServiceProvider.GetRequiredService<IWorkScheduleReadService>().GetForDateAsync(employeeId, Monday, TestContext.Current.CancellationToken);
        day.IsWorkingDay.ShouldBeTrue();
        return (day.Start!.Value, day.End!.Value);
    }

    /// <summary>Seeds the day through the real "Çalışılan saat" manual record path (same upsert the correction uses).</summary>
    private async Task RecordAsync(Guid employeeId, TimeOnly? checkIn, TimeOnly? checkOut)
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<RecordAttendanceHandler>(scope.ServiceProvider, _dataScope, _localClock);
        var recorded = await handler.Handle(new RecordAttendanceCommand(employeeId, Monday, checkIn, checkOut), TestContext.Current.CancellationToken);
        recorded.IsSuccess.ShouldBeTrue(recorded.IsFailure ? recorded.Error.Code : null);
        await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Result<PunchCorrectionRequestDto>> SubmitAsync(SubmitMyPunchCorrectionCommand command)
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<SubmitMyPunchCorrectionHandler>(
            scope.ServiceProvider, _currentUser, _workflow, (ITenantLocalClock)_localClock);
        var result = await handler.Handle(command, TestContext.Current.CancellationToken);
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return result;
    }

    private async Task<AttendanceDay> FindDayAsync(Guid employeeId)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        return await db.AttendanceDays.AsNoTracking().SingleAsync(d => d.EmployeeId == employeeId && d.Date == Monday, TestContext.Current.CancellationToken);
    }

    private async Task<PunchCorrectionRequest> FindRequestAsync(Guid id)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        return await db.PunchCorrectionRequests.AsNoTracking().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }
}
