using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
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

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveRequestStateTests
{
    private static LeaveRequest NewRequest() =>
        LeaveRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Annual, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), 5m,
            null, LeaveRequestSource.SelfService, DateTime.UtcNow, "10.0.0.1", null).Value;

    [Fact]
    public void Submit_RejectsInvertedRange()
    {
        LeaveRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Annual, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 5), 1m,
            null, LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidRange);
    }

    [Fact]
    public void Submit_Hourly_StoresTheTimeRange()
    {
        var day = new DateOnly(2026, 10, 5);
        var submitted = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Excuse, day, day, 0.2667m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(11, 0));

        submitted.IsSuccess.ShouldBeTrue();
        submitted.Value.StartTime.ShouldBe(new TimeOnly(9, 0));
        submitted.Value.EndTime.ShouldBe(new TimeOnly(11, 0));
        submitted.Value.HalfDayPeriod.ShouldBeNull();
    }

    [Fact]
    public void Submit_HalfDay_StoresThePeriod()
    {
        var day = new DateOnly(2026, 10, 5);
        var submitted = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Excuse, day, day, 0.5m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, halfDayPeriod: HalfDayPeriod.Afternoon);

        submitted.IsSuccess.ShouldBeTrue();
        submitted.Value.HalfDayPeriod.ShouldBe(HalfDayPeriod.Afternoon);
        submitted.Value.StartTime.ShouldBeNull();
    }

    [Fact]
    public void Submit_HourlyAcrossTwoDays_IsRejected()
    {
        LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Excuse, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), 0.2667m, null,
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(11, 0))
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayRequiresSingleDate);
    }

    [Fact]
    public void Submit_HourlyWithEndBeforeStart_IsRejected()
    {
        var day = new DateOnly(2026, 10, 5);
        LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Excuse, day, day, 0.2667m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, startTime: new TimeOnly(11, 0), endTime: new TimeOnly(9, 0))
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayTimeInvalid);
    }

    [Fact]
    public void Submit_BothHourlyAndHalfDay_IsRejected()
    {
        var day = new DateOnly(2026, 10, 5);
        LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Excuse, day, day, 0.5m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(11, 0), halfDayPeriod: HalfDayPeriod.Morning)
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayTimeInvalid);
    }

    [Fact]
    public void ConflictsWith_SameDayNonOverlappingHourlyRanges_DoNotConflict()
    {
        var day = new DateOnly(2026, 10, 5);
        var existing = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.JobSearch, day, day, 0.1333m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(10, 0)).Value;

        existing.ConflictsWith(day, day, new TimeOnly(11, 0), new TimeOnly(12, 0), null).ShouldBeFalse();
    }

    [Fact]
    public void ConflictsWith_SameDayOverlappingHourlyRanges_Conflicts()
    {
        var day = new DateOnly(2026, 10, 5);
        var existing = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.JobSearch, day, day, 0.1333m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(11, 0)).Value;

        existing.ConflictsWith(day, day, new TimeOnly(10, 0), new TimeOnly(12, 0), null).ShouldBeTrue();
    }

    [Fact]
    public void ConflictsWith_MorningHalfDay_AndAfternoonHourly_DoNotConflict_ButAnHourlyCrossingNoonDoes()
    {
        var day = new DateOnly(2026, 10, 5);
        var existing = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.Excuse, day, day, 0.5m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, halfDayPeriod: HalfDayPeriod.Morning).Value;

        existing.ConflictsWith(day, day, new TimeOnly(13, 0), new TimeOnly(15, 0), null).ShouldBeFalse();
        existing.ConflictsWith(day, day, null, null, HalfDayPeriod.Afternoon).ShouldBeFalse();
        existing.ConflictsWith(day, day, new TimeOnly(11, 0), new TimeOnly(13, 0), null).ShouldBeTrue();
    }

    [Fact]
    public void ConflictsWith_FullDayRequest_AlwaysConflictsOnASharedDate_EvenAgainstAPartialCandidate()
    {
        var existing = NewRequest(); // FullDay, 2026-10-05..2026-10-09
        existing.ConflictsWith(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), new TimeOnly(9, 0), new TimeOnly(10, 0), null).ShouldBeTrue();
    }

    [Fact]
    public void ConflictsWith_DifferentDates_NeverConflicts()
    {
        var day = new DateOnly(2026, 10, 5);
        var existing = LeaveRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), LeaveTypeCode.JobSearch, day, day, 0.1333m, null, LeaveRequestSource.SelfService,
            DateTime.UtcNow, null, null, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(11, 0)).Value;

        existing.ConflictsWith(day.AddDays(1), day.AddDays(1), new TimeOnly(9, 0), new TimeOnly(11, 0), null).ShouldBeFalse();
    }

    [Fact]
    public void UpdatePending_BumpsRevision_AndOldApprovalDecisionIsIgnored()
    {
        var request = NewRequest();
        var firstApproval = Guid.NewGuid();
        request.AttachApproval(firstApproval);

        request.UpdatePending(request.LeaveTypeId, LeaveTypeCode.Annual, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), 2m, "kısaltıldı").IsSuccess.ShouldBeTrue();
        request.RevisionNo.ShouldBe(1);
        request.ApprovalRequestId.ShouldBeNull();
        var secondApproval = Guid.NewGuid();
        request.AttachApproval(secondApproval);

        request.Approve(firstApproval, DateTime.UtcNow).Error.Code.ShouldBe(LeaveDomainErrors.RequestApprovalMismatch);
        request.Status.ShouldBe(LeaveRequestStatus.Pending);
        request.Approve(secondApproval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.UpdatePending(request.LeaveTypeId, LeaveTypeCode.Annual, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 13), 2m, null).Error.Code
            .ShouldBe(LeaveDomainErrors.RequestNotPending);
        request.Withdraw(DateTime.UtcNow).IsFailure.ShouldBeTrue();
    }
}

/// <summary>Submit → Workflow start (substituted IWorkflowService) → ApprovalCompleted/ApprovalRejected through IEventBus, on real PostgreSQL.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveRequestIntegrationTests : TestFixture
{
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        // AddLeaveServices' IWorkScheduleReader now adapts Attendance.Contracts.IWorkScheduleReadService (roadmap #23).
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        // roadmap #23 (A1): EmployeeHired -> default schedule assignment must run here too, otherwise the
        // real IWorkScheduleReadService reports every day closed and all submits fail with zero_days.
        services.AddAttendanceIntegrationEventHandlers();
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly, typeof(ILeaveRuleSetRepository).Assembly, typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);

        var holidays = Substitute.For<IHolidayCalendarLookup>();
        holidays.ResolveCalendarAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((HolidayCalendarRef?)null);
        services.AddSingleton(holidays);

        // PreviewMyLeaveRequestHandler's teamOverlap lookup (faz-05 tur 3, S-7); unconfigured GetOrgUnitIdAsync
        // returns null (NSubstitute default for Task<Guid?>) so TeamOverlap stays null - existing tests don't assert it.
        services.AddSingleton(Substitute.For<IEmployeeLookup>());
        services.AddSingleton(Substitute.For<IEmployeeDirectory>());

        // senseik-007 (age-50-exception): EmployeeHiredHandler needs IEmployeeBirthDateLookup - same registration as
        // OvertimeTests/LeaveBalanceRecalculationJobTests; without it every EmployeeHired publish fails DI resolution.
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
        services.AddScoped<IEmployeeBirthDateLookup, EmployeeBirthDateLookup>();
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private async Task<Result<LeaveRequestDto>> SubmitAsync(
        Guid tenantId, Guid leaveTypeId, DateOnly start, DateOnly end, TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        using var scope = ServiceProvider!.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<SubmitLeaveRequestHandler>(scope.ServiceProvider, _currentUser, _workflow);
        var result = await handler.Handle(
            new SubmitLeaveRequestCommand(leaveTypeId, start, end, null, StartTime: startTime, EndTime: endTime, HalfDayPeriod: halfDayPeriod), CancellationToken.None);
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    [Fact]
    public async Task Submit_Approve_PostsUsageAtStartDate_AndValidatesBalanceAndOverlap()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        await bus.Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        // 14 days accrued; Mon..Fri of a future week = 5 working days + the Saturday rule (full Mon–Fri
        // taken with the default schedule's IncludeSaturdayWhenFullWeek flag) = 6 leave days.
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        var submitted = await SubmitAsync(tenantId, annualId, monday, monday.AddDays(4));
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);

        (await SubmitAsync(tenantId, annualId, monday.AddDays(2), monday.AddDays(3))).Error.Code.ShouldBe(LeaveRequestErrors.Overlap);
        (await SubmitAsync(tenantId, annualId, monday.AddDays(7), monday.AddDays(7 + 13))).Error.Code.ShouldBe(LeaveRequestErrors.InsufficientBalance);

        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Leave, submitted.Value.Id, employeeId));

        using var verify = ServiceProvider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = await db.Requests.SingleAsync(r => r.Id == submitted.Value.Id);
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.CalculatedDays.ShouldBe(6m);
        request.ApprovalRequestId.ShouldBe(approvalId);
        var usage = await db.LedgerEntries.SingleAsync(e => e.Type == LeaveLedgerEntryType.Usage);
        usage.Amount.ShouldBe(-6m);
        usage.EffectiveDate.ShouldBe(monday);
        usage.SourceRefId.ShouldBe(submitted.Value.Id);
    }

    [Fact]
    public async Task RequestableTypes_Preview_AndOnBehalfWithBypass_PostUsageWithoutWorkflow()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCreateOnBehalf.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);

        using var scope = ServiceProvider.CreateScope();
        var types = await ActivatorUtilities.CreateInstance<ListMyRequestableLeaveTypesHandler>(scope.ServiceProvider, _currentUser, dataScope)
            .Handle(new ListMyRequestableLeaveTypesQuery(), CancellationToken.None);
        types.IsSuccess.ShouldBeTrue();
        types.Value.ShouldContain(t => t.Code == LeaveTypeCode.Annual && t.Name == "leave.type.annual" && t.HasBalance);
        var annualId = types.Value.Single(t => t.Code == LeaveTypeCode.Annual).Id;

        var preview = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, monday, monday.AddDays(6), null), CancellationToken.None);
        // Mon..Sun: 5 working days + Saturday appended by the Saturday rule (full week, flag on) = 6; only Sunday rests.
        preview.Value.Days.ShouldBe(6m);
        preview.Value.WeeklyRestDays.ShouldBe(1);
        preview.Value.Warnings.ShouldBeEmpty();
        var tooLong = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, monday, monday.AddDays(27), null), CancellationToken.None);
        tooLong.Value.Warnings.Single().Code.ShouldBe(LeaveRequestErrors.InsufficientBalance);

        var onBehalf = ActivatorUtilities.CreateInstance<CreateLeaveRequestOnBehalfHandler>(scope.ServiceProvider, _currentUser, dataScope, _workflow);
        (await onBehalf.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(1), null, true), CancellationToken.None))
            .Error.Code.ShouldBe(LeaveRequestErrors.BypassNotAllowed);
        dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsBypassApproval.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var created = await onBehalf.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(1), null, true), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
        await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);

        using var verify = ServiceProvider.CreateScope();
        var mine = await ActivatorUtilities.CreateInstance<ListMyLeaveRequestsHandler>(verify.ServiceProvider, _currentUser)
            .Handle(new ListMyLeaveRequestsQuery(), CancellationToken.None);
        var request = mine.Value.Single();
        request.Status.ShouldBe(LeaveRequestStatus.Approved);
        request.Source.ShouldBe(LeaveRequestSource.OnBehalf);
        request.LeaveTypeName.ShouldBe("leave.type.annual");
        var ledger = await ActivatorUtilities.CreateInstance<Sense.Modules.Leave.Application.Balances.ListMyLeaveLedgerHandler>(verify.ServiceProvider, _currentUser)
            .Handle(new Sense.Modules.Leave.Application.Balances.ListMyLeaveLedgerQuery(annualId), CancellationToken.None);
        ledger.Value.ShouldContain(e => e.Type == LeaveLedgerEntryType.Usage && e.Amount == -2m && e.SourceRefId == created.Value.Id);
    }

    /// <summary>
    /// senseik-037: IHolidayCalendarLookup.GetLastDefinedReligiousHolidayYearAsync flags a request landing in a year
    /// the tenant's calendar has no religious-holiday (Ramazan/Kurban Bayramı) entries for - a non-blocking warning
    /// (same channel as InsufficientBalance above), not a rejection: the request must still preview/submit fine.
    /// </summary>
    [Fact]
    public async Task Preview_WarnsWhenRequestFallsInAYearTheReligiousHolidayCalendarHasNoDataFor()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        // Calendar's religious-holiday data stops last year - any request touching this year (or later) is undefined.
        var holidays = ServiceProvider.GetRequiredService<IHolidayCalendarLookup>();
        holidays.GetLastDefinedReligiousHolidayYearAsync(tenantId, Arg.Any<CancellationToken>()).Returns(today.Year - 1);

        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        using var scope = ServiceProvider.CreateScope();
        var preview = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, monday, monday.AddDays(1), null), CancellationToken.None);

        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Error.Code : null);
        var warning = preview.Value.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LeaveRequestErrors.HolidayCalendarYearUndefined);
        warning.Args.ShouldNotBeNull();
        warning.Args!["year"].ShouldBe(monday.Year.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>S-7 teamOverlap: same org unit + active (Pending/Approved) only; rejected/cancelled/different-unit peers are excluded.</summary>
    [Fact]
    public async Task Preview_TeamOverlap_CountsSameUnitActiveOnly()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        var orgUnitId = Guid.NewGuid();
        var peerApproved = Guid.NewGuid();
        var peerPending = Guid.NewGuid();
        var peerRejected = Guid.NewGuid();
        var peerCancelled = Guid.NewGuid();
        var peerOtherUnit = Guid.NewGuid(); // exists but not returned by ListActiveEmployeeIdsInOrgUnitsAsync below - simulates "different unit".

        var employeeLookup = ServiceProvider.GetRequiredService<IEmployeeLookup>();
        employeeLookup.GetOrgUnitIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        var directory = ServiceProvider.GetRequiredService<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Contains(orgUnitId)), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { peerApproved, peerPending, peerRejected, peerCancelled });
        directory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact>
            {
                new(peerApproved, "Ayşe", "Approved", null, null, null, "Active"),
                new(peerPending, "Bora", "Pending", null, null, null, "Active"),
            });

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        var window = (Start: monday, End: monday.AddDays(4));

        LeaveRequest MakePeerRequest(Guid peerId) =>
            LeaveRequest.Submit(tenantId, peerId, Guid.NewGuid(), LeaveTypeCode.Annual, window.Start, window.End, 5m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;

        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            var approved = MakePeerRequest(peerApproved);
            var approvalId1 = Guid.NewGuid();
            approved.AttachApproval(approvalId1);
            approved.Approve(approvalId1, DateTime.UtcNow);
            db.Requests.Add(approved);

            db.Requests.Add(MakePeerRequest(peerPending)); // stays Pending

            var rejected = MakePeerRequest(peerRejected);
            var approvalId2 = Guid.NewGuid();
            rejected.AttachApproval(approvalId2);
            rejected.Reject(approvalId2, DateTime.UtcNow);
            db.Requests.Add(rejected);

            var cancelled = MakePeerRequest(peerCancelled);
            cancelled.Cancel("Vazgeçildi", peerCancelled, DateTime.UtcNow);
            db.Requests.Add(cancelled);

            db.Requests.Add(MakePeerRequest(peerOtherUnit)); // Pending but not in the mocked peers list.

            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        var preview = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, window.Start, window.End, null), CancellationToken.None);

        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Error.Code : null);
        preview.Value.TeamOverlap.ShouldNotBeNull();
        preview.Value.TeamOverlap!.Count.ShouldBe(2);
        preview.Value.TeamOverlap.Employees.Select(e => e.EmployeeId).ShouldBe([peerApproved, peerPending], ignoreOrder: true);
    }

    [Fact]
    public async Task ApprovalRejected_ForLeave_MarksRejected_AndIgnoresOtherRequestTypes()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        Guid requestId;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = LeaveRequest.Submit(tenantId, employeeId, Guid.NewGuid(), LeaveTypeCode.Excuse, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), 1m,
                null, LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            request.AttachApproval(approvalId);
            db.Requests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var bus = ServiceProvider.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Advance, requestId, employeeId));
        await bus.Publish(new ApprovalRejected(tenantId, approvalId, ApprovalRequestTypes.Leave, requestId, employeeId, 1));

        using var verify = ServiceProvider.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().Requests.SingleAsync(r => r.Id == requestId);
        stored.Status.ShouldBe(LeaveRequestStatus.Rejected);
        (await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().LedgerEntries.CountAsync()).ShouldBe(0);
    }

    /// <summary>"leave.type.job_search" is seeded with Unit=Hourly (11.3.5 seed list) - no rule set edit needed for this slice.</summary>
    [Fact]
    public async Task Submit_Hourly_CalculatesFractionalDays_AndRejectsAMultiDayRange()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _currentUser.EmployeeId.Returns(employeeId);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, today.AddYears(-1), today.AddYears(-1)));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid jobSearchId;
        using (var read = ServiceProvider.CreateScope())
        {
            jobSearchId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.JobSearch).Id;
        }

        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);

        // 2 of the default 450-minute workday = 120/450, rounded to the ledger's 4-decimal precision.
        var submitted = await SubmitAsync(tenantId, jobSearchId, monday, monday, new TimeOnly(9, 0), new TimeOnly(11, 0));
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
        submitted.Value.CalculatedDays.ShouldBe(0.2667m);
        submitted.Value.StartTime.ShouldBe(new TimeOnly(9, 0));
        submitted.Value.EndTime.ShouldBe(new TimeOnly(11, 0));

        var multiDay = await SubmitAsync(tenantId, jobSearchId, monday, monday.AddDays(1), new TimeOnly(9, 0), new TimeOnly(11, 0));
        multiDay.Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayRequiresSingleDate);

        var bus = ServiceProvider.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Leave, submitted.Value.Id, employeeId));

        using var verify = ServiceProvider.CreateScope();
        var usage = await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().LedgerEntries.SingleAsync(e => e.Type == LeaveLedgerEntryType.Usage);
        usage.Amount.ShouldBe(-0.2667m);
    }

    /// <summary>
    /// Before LeaveRequest.ConflictsWith, the "leave.errors.overlap" gate only looked at the date range, so a
    /// second same-day Hourly request was always rejected regardless of the actual time window. Now only a real
    /// time intersection conflicts.
    /// </summary>
    [Fact]
    public async Task Submit_TwoHourlyRequestsSameDay_OnlyConflictsWhenTimesActuallyIntersect()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _currentUser.EmployeeId.Returns(employeeId);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, today.AddYears(-1), today.AddYears(-1)));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid jobSearchId;
        using (var read = ServiceProvider.CreateScope())
        {
            jobSearchId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.JobSearch).Id;
        }

        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);

        var first = await SubmitAsync(tenantId, jobSearchId, monday, monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Code : null);

        // Non-overlapping window, same day: both requests stay active, but the time ranges do not intersect.
        var nonOverlapping = await SubmitAsync(tenantId, jobSearchId, monday, monday, new TimeOnly(11, 0), new TimeOnly(12, 0));
        nonOverlapping.IsSuccess.ShouldBeTrue(nonOverlapping.IsFailure ? nonOverlapping.Error.Code : null);

        // Overlapping window: still rejected.
        var overlapping = await SubmitAsync(tenantId, jobSearchId, monday, monday, new TimeOnly(11, 30), new TimeOnly(12, 30));
        overlapping.Error.Code.ShouldBe(LeaveRequestErrors.Overlap);
    }

    /// <summary>
    /// LeaveRequestRules.ValidatePartialDayShape gap (faz-05 tur 1 madde 5): an Hourly-unit type ("leave.type.job_search")
    /// submitted without a time range is rejected before LeaveRequest.Submit is even reached.
    /// </summary>
    [Fact]
    public async Task Submit_HourlyType_WithoutTimeRange_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _currentUser.EmployeeId.Returns(employeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, today.AddYears(-1), today.AddYears(-1)));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid jobSearchId;
        using (var read = ServiceProvider.CreateScope())
        {
            jobSearchId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.JobSearch).Id;
        }

        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        (await SubmitAsync(tenantId, jobSearchId, monday, monday)).Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayTimeInvalid);
    }

    /// <summary>
    /// LeaveRequestRules.ValidatePartialDayShape gap: a FullDay-unit type ("leave.type.annual") rejects a time range or
    /// half-day period - current (unchanged) behaviour, not a "leave request without time fields" special case.
    /// </summary>
    [Fact]
    public async Task Submit_FullDayType_WithTimeRange_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        (await SubmitAsync(tenantId, annualId, monday, monday, new TimeOnly(9, 0), new TimeOnly(11, 0))).Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayTimeInvalid);
    }

    /// <summary>
    /// LeaveRequestRules.ValidatePartialDayShape gap: a HalfDay-unit type needs a period and a single day (no seeded
    /// type uses Unit=HalfDay - "leave.type.job_search" is the only non-FullDay seed and it is Hourly), and the
    /// successful path calculates the fixed <see cref="LeavePartialDay.HalfDayFraction"/> (0.5 gün).
    /// </summary>
    [Fact]
    public async Task Submit_HalfDayType_RequiresPeriodAndSingleDay_AndCalculatesHalfADay()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _currentUser.EmployeeId.Returns(employeeId);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, today.AddYears(-1), today.AddYears(-1)));

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid halfDayTypeId;
        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSetId = (await db.RuleSets.SingleAsync()).Id;
            var settings = new LeaveTypeSettings(LeaveTypeCode.Custom, "Yarım gün izni", null, true, LeaveLimitType.Unlimited, Unit: LeaveUnit.HalfDay);
            var added = await ActivatorUtilities.CreateInstance<AddLeaveTypeHandler>(write.ServiceProvider)
                .Handle(new AddLeaveTypeCommand(ruleSetId, settings), CancellationToken.None);
            added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Code : null);
            halfDayTypeId = added.Value;
            await db.SaveChangesAsync();
        }

        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);

        (await SubmitAsync(tenantId, halfDayTypeId, monday, monday)).Error.Code.ShouldBe(LeaveDomainErrors.RequestPartialDayTimeInvalid);
        (await SubmitAsync(tenantId, halfDayTypeId, monday, monday.AddDays(1), halfDayPeriod: HalfDayPeriod.Morning)).Error.Code
            .ShouldBe(LeaveDomainErrors.RequestPartialDayRequiresSingleDate);

        var submitted = await SubmitAsync(tenantId, halfDayTypeId, monday, monday, halfDayPeriod: HalfDayPeriod.Morning);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
        submitted.Value.CalculatedDays.ShouldBe(0.5m);
    }

    /// <summary>
    /// OverlappingApprovedCount in PreviewDto: count only approved (not pending) leave on the same start date,
    /// same org unit, excluding the caller.
    /// </summary>
    [Fact]
    public async Task Preview_OverlappingApprovedCount_OnSameStartDate_Returns2()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        var orgUnitId = Guid.NewGuid();
        var peerApproved1 = Guid.NewGuid();
        var peerApproved2 = Guid.NewGuid();
        var peerPending = Guid.NewGuid();

        var employeeLookup = ServiceProvider.GetRequiredService<IEmployeeLookup>();
        employeeLookup.GetOrgUnitIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        var directory = ServiceProvider.GetRequiredService<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Contains(orgUnitId)), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { peerApproved1, peerApproved2, peerPending });
        directory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact>());

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var testDate = today.AddDays(14);

        LeaveRequest MakePeerRequest(Guid peerId) =>
            LeaveRequest.Submit(tenantId, peerId, Guid.NewGuid(), LeaveTypeCode.Annual, testDate, testDate, 1m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;

        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            var approved1 = MakePeerRequest(peerApproved1);
            var approvalId1 = Guid.NewGuid();
            approved1.AttachApproval(approvalId1);
            approved1.Approve(approvalId1, DateTime.UtcNow);
            db.Requests.Add(approved1);

            var approved2 = MakePeerRequest(peerApproved2);
            var approvalId2 = Guid.NewGuid();
            approved2.AttachApproval(approvalId2);
            approved2.Approve(approvalId2, DateTime.UtcNow);
            db.Requests.Add(approved2);

            db.Requests.Add(MakePeerRequest(peerPending)); // stays Pending

            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        var preview = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, testDate, testDate, null), CancellationToken.None);

        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Error.Code : null);
        preview.Value.OverlappingApprovedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Preview_OverlappingApprovedCount_NoOverlap_Returns0()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(employeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));

        var orgUnitId = Guid.NewGuid();
        var peerApproved = Guid.NewGuid();

        var employeeLookup = ServiceProvider.GetRequiredService<IEmployeeLookup>();
        employeeLookup.GetOrgUnitIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        var directory = ServiceProvider.GetRequiredService<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Contains(orgUnitId)), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { peerApproved });
        directory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact>());

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var testDateOther = today.AddDays(14);
        var previewDate = today.AddDays(20);

        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            var approved = LeaveRequest.Submit(tenantId, peerApproved, Guid.NewGuid(), LeaveTypeCode.Annual, testDateOther, testDateOther, 1m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            var approvalId = Guid.NewGuid();
            approved.AttachApproval(approvalId);
            approved.Approve(approvalId, DateTime.UtcNow);
            db.Requests.Add(approved);

            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        Guid annualId;
        using (var read = ServiceProvider.CreateScope())
        {
            annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        }

        var preview = await ActivatorUtilities.CreateInstance<PreviewMyLeaveRequestHandler>(scope.ServiceProvider, _currentUser)
            .Handle(new PreviewMyLeaveRequestQuery(annualId, previewDate, previewDate, null), CancellationToken.None);

        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Error.Code : null);
        preview.Value.OverlappingApprovedCount.ShouldBe(0);
    }

    /// <summary>
    /// Calendar entries include OverlappingApprovedCount: approved leave on the same start date from the same org unit,
    /// excluding the entry's own employee.
    /// </summary>
    [Fact]
    public async Task GetLeaveCalendarHandler_IncludesOverlappingApprovedCount()
    {
        var tenantId = Guid.NewGuid();
        var mainEmployeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(mainEmployeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, mainEmployeeId, accrualStart, accrualStart));

        var orgUnitId = Guid.NewGuid();
        var peerApproved1 = Guid.NewGuid();
        var peerApproved2 = Guid.NewGuid();

        var employeeLookup = ServiceProvider.GetRequiredService<IEmployeeLookup>();
        employeeLookup.GetOrgUnitIdAsync(tenantId, mainEmployeeId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        employeeLookup.GetOrgUnitIdAsync(tenantId, peerApproved1, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        employeeLookup.GetOrgUnitIdAsync(tenantId, peerApproved2, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        var directory = ServiceProvider.GetRequiredService<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Contains(orgUnitId)), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { mainEmployeeId, peerApproved1, peerApproved2 });
        directory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> {
                new(mainEmployeeId, "Main", "Employee", null, null, null, "active"),
                new(peerApproved1, "Peer", "One", null, null, null, "active"),
                new(peerApproved2, "Peer", "Two", null, null, null, "active")
            });

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var testDate = today.AddDays(14);
        var calendarFrom = testDate;
        var calendarTo = testDate.AddDays(6);

        LeaveRequest MakeRequest(Guid employeeId) =>
            LeaveRequest.Submit(tenantId, employeeId, Guid.NewGuid(), LeaveTypeCode.Annual, testDate, testDate, 1m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;

        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            // Main employee's request (Approved)
            var mainRequest = MakeRequest(mainEmployeeId);
            var mainApprovalId = Guid.NewGuid();
            mainRequest.AttachApproval(mainApprovalId);
            mainRequest.Approve(mainApprovalId, DateTime.UtcNow);
            db.Requests.Add(mainRequest);

            // Peer 1 (Approved) — should appear in calendar and show count of other approved on same date
            var peer1Request = MakeRequest(peerApproved1);
            var peer1ApprovalId = Guid.NewGuid();
            peer1Request.AttachApproval(peer1ApprovalId);
            peer1Request.Approve(peer1ApprovalId, DateTime.UtcNow);
            db.Requests.Add(peer1Request);

            // Peer 2 (Approved) — should appear in calendar and show count of other approved on same date
            var peer2Request = MakeRequest(peerApproved2);
            var peer2ApprovalId = Guid.NewGuid();
            peer2Request.AttachApproval(peer2ApprovalId);
            peer2Request.Approve(peer2ApprovalId, DateTime.UtcNow);
            db.Requests.Add(peer2Request);

            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<GetLeaveCalendarHandler>(scope.ServiceProvider, _currentUser);
        var result = await handler.Handle(new GetLeaveCalendarQuery(calendarFrom, calendarTo, orgUnitId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        // Main employee sees peer1 and peer2 on the same date
        result.Value.Count.ShouldBeGreaterThanOrEqualTo(2);
        var peerEntry1 = result.Value.FirstOrDefault(e => e.EmployeeId == peerApproved1);
        var peerEntry2 = result.Value.FirstOrDefault(e => e.EmployeeId == peerApproved2);
        peerEntry1.ShouldNotBeNull();
        peerEntry2.ShouldNotBeNull();
        // Each peer should see count = 2 (the main employee + the other peer on same date)
        peerEntry1.OverlappingApprovedCount.ShouldBe(2);
        peerEntry2.OverlappingApprovedCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetLeaveCalendarHandler_NoOverlap_OverlappingApprovedCountIs0()
    {
        var tenantId = Guid.NewGuid();
        var mainEmployeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accrualStart = today.AddYears(-1).AddDays(-10);
        _currentUser.EmployeeId.Returns(mainEmployeeId);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, mainEmployeeId, accrualStart, accrualStart));

        var orgUnitId = Guid.NewGuid();
        var peerApproved = Guid.NewGuid();

        var employeeLookup = ServiceProvider.GetRequiredService<IEmployeeLookup>();
        employeeLookup.GetOrgUnitIdAsync(tenantId, mainEmployeeId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        employeeLookup.GetOrgUnitIdAsync(tenantId, peerApproved, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        var directory = ServiceProvider.GetRequiredService<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Contains(orgUnitId)), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { mainEmployeeId, peerApproved });
        directory.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> {
                new(mainEmployeeId, "Main", "Employee", null, null, null, "active"),
                new(peerApproved, "Peer", "One", null, null, null, "active")
            });

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var testDateMain = today.AddDays(14);
        var testDatePeer = today.AddDays(20); // Different date
        var calendarFrom = testDateMain;
        var calendarTo = testDatePeer;

        using (var write = ServiceProvider.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            // Main employee's request on day 14
            var mainRequest = LeaveRequest.Submit(tenantId, mainEmployeeId, Guid.NewGuid(), LeaveTypeCode.Annual, testDateMain, testDateMain, 1m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            var mainApprovalId = Guid.NewGuid();
            mainRequest.AttachApproval(mainApprovalId);
            mainRequest.Approve(mainApprovalId, DateTime.UtcNow);
            db.Requests.Add(mainRequest);

            // Peer's request on day 20 (different)
            var peerRequest = LeaveRequest.Submit(tenantId, peerApproved, Guid.NewGuid(), LeaveTypeCode.Annual, testDatePeer, testDatePeer, 1m, null,
                LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;
            var peerApprovalId = Guid.NewGuid();
            peerRequest.AttachApproval(peerApprovalId);
            peerRequest.Approve(peerApprovalId, DateTime.UtcNow);
            db.Requests.Add(peerRequest);

            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<GetLeaveCalendarHandler>(scope.ServiceProvider, _currentUser);
        var result = await handler.Handle(new GetLeaveCalendarQuery(calendarFrom, calendarTo, orgUnitId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Count.ShouldBeGreaterThanOrEqualTo(1);
        var peerEntry = result.Value.FirstOrDefault(e => e.EmployeeId == peerApproved);
        peerEntry.ShouldNotBeNull();
        // Peer's leave is on a different date, so no overlapping approved
        peerEntry.OverlappingApprovedCount.ShouldBe(0);
    }
}
