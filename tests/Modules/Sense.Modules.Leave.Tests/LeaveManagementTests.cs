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
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
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

public sealed class LeaveManagementDomainTests
{
    private static readonly DateOnly Start = new(2026, 10, 5);

    private static LeaveRequest NewRequest(Guid? employeeId = null, Guid? typeId = null, DateOnly? start = null) =>
        LeaveRequest.Submit(Guid.NewGuid(), employeeId ?? Guid.NewGuid(), typeId ?? Guid.NewGuid(), LeaveTypeCode.Annual, start ?? Start, (start ?? Start).AddDays(4), 5m,
            null, LeaveRequestSource.SelfService, DateTime.UtcNow, null, null).Value;

    [Fact]
    public void Cancel_FromPendingAndApproved_SetsCancellationFields()
    {
        var userId = Guid.NewGuid();
        var at = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
        var pending = NewRequest();
        pending.Cancel("  plan değişti ", userId, at).IsSuccess.ShouldBeTrue();
        pending.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        pending.CancelledAt.ShouldBe(at);
        pending.CancellationReason.ShouldBe("plan değişti");
        pending.CancelledByUserId.ShouldBe(userId);

        var approved = NewRequest();
        approved.ApproveWithoutWorkflow(at).IsSuccess.ShouldBeTrue();
        approved.Cancel("iptal", userId, at).IsSuccess.ShouldBeTrue();
        approved.Status.ShouldBe(LeaveRequestStatus.Cancelled);
        approved.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Cancel_TerminalStates_AreNotCancellable_AndReasonIsRequired()
    {
        var approval = Guid.NewGuid();
        var rejected = NewRequest();
        rejected.AttachApproval(approval);
        rejected.Reject(approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        rejected.Cancel("x", null, DateTime.UtcNow).Error.Code.ShouldBe(LeaveDomainErrors.RequestNotCancellable);

        var withdrawn = NewRequest();
        withdrawn.Withdraw(DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        withdrawn.Cancel("x", null, DateTime.UtcNow).Error.Code.ShouldBe(LeaveDomainErrors.RequestNotCancellable);

        var cancelled = NewRequest();
        cancelled.Cancel("x", null, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        cancelled.Cancel("x", null, DateTime.UtcNow).Error.Code.ShouldBe(LeaveDomainErrors.RequestNotCancellable);

        var pending = NewRequest();
        pending.Cancel(" ", null, DateTime.UtcNow).Error.Code.ShouldBe(LeaveDomainErrors.RequestCancellationReasonRequired);
        pending.Cancel(new string('a', LeaveLimits.CancellationReasonMaxLength + 1), null, DateTime.UtcNow).Error.Code
            .ShouldBe(LeaveDomainErrors.RequestCancellationReasonRequired);
        pending.Status.ShouldBe(LeaveRequestStatus.Pending);
    }

    [Fact]
    public void Reversal_RestoresUsage_OnceAndNetsOutInProjection()
    {
        var set = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(Guid.NewGuid(), StatutoryLeaveDurations.Turkey2026);
        var annual = set.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        var request = NewRequest(typeId: annual.Id, start: new DateOnly(2026, 3, 2));
        var usage = LeaveLedgerEntry.Usage(request.TenantId, request.EmployeeId, annual.Id, 5m, request.StartDate, LeaveLedgerSource.Request, request.Id);
        var other = LeaveLedgerEntry.Usage(request.TenantId, request.EmployeeId, annual.Id, 1m, request.StartDate, LeaveLedgerSource.Request, Guid.NewGuid());
        var accrual = LeaveLedgerEntry.Accrual(request.TenantId, request.EmployeeId, annual.Id, 14m, new DateOnly(2026, 1, 1));

        var reversals = LeaveCancellation.Reversals(request, [accrual, usage, other], "iptal");
        var reversal = reversals.ShouldHaveSingleItem();
        reversal.Type.ShouldBe(LeaveLedgerEntryType.Reversal);
        reversal.Amount.ShouldBe(5m);
        reversal.EffectiveDate.ShouldBe(request.StartDate);
        reversal.LeaveTypeId.ShouldBe(annual.Id);
        reversal.Source.ShouldBe(LeaveLedgerSource.Request);
        reversal.SourceRefId.ShouldBe(request.Id);
        reversal.IsGenerated.ShouldBeFalse();
        LeaveCancellation.Reversals(request, [accrual, usage, other, reversal], "iptal").ShouldBeEmpty();

        var accrualStart = new DateOnly(2025, 1, 1);
        var today = new DateOnly(2026, 6, 1);
        var before = LeaveBalanceEngine.Project(annual, accrualStart, today, [accrual, usage, other]);
        var after = LeaveBalanceEngine.Project(annual, accrualStart, today, [accrual, usage, other, reversal]);
        before.CurrentPeriodUsed.ShouldBe(6m);
        after.CurrentPeriodUsed.ShouldBe(1m);
        after.TotalUsed.ShouldBe(1m);
        after.CurrentBalance.ShouldBe(before.CurrentBalance + 5m);
    }

    [Fact]
    public void RuleSet_DeleteGuards_AndSingleDefault()
    {
        var tenantId = Guid.NewGuid();
        var first = LeaveRuleSet.Create(tenantId, "Varsayılan", isDefault: true).Value;
        var second = LeaveRuleSet.Create(tenantId, "İkinci").Value;
        var third = LeaveRuleSet.Create(tenantId, "Üçüncü").Value;
        LeaveRuleSet[] all = [first, second, third];

        first.EnsureDeletable(0).Error.Code.ShouldBe(LeaveDomainErrors.RuleSetIsDefault);
        second.EnsureDeletable(3).Error.Code.ShouldBe(LeaveDomainErrors.RuleSetHasEmployees);
        second.EnsureDeletable(0).IsSuccess.ShouldBeTrue();

        second.MakeDefault(all);
        all.Count(s => s.IsDefault).ShouldBe(1);
        second.IsDefault.ShouldBeTrue();
        first.EnsureDeletable(0).IsSuccess.ShouldBeTrue();
        third.MakeDefault(all);
        all.Single(s => s.IsDefault).ShouldBe(third);

        second.Rename(" Yeni ad ").IsSuccess.ShouldBeTrue();
        second.Name.ShouldBe("Yeni ad");
        second.Rename("").Error.Code.ShouldBe(LeaveDomainErrors.RuleSetNameRequired);
    }

    [Fact]
    public void Calendar_ValidatesRange()
    {
        LeaveCalendar.ValidateRange(Start, Start.AddDays(-1)).Error.Code.ShouldBe(LeaveDomainErrors.RequestInvalidRange);
        LeaveCalendar.ValidateRange(Start, Start.AddDays(LeaveLimits.CalendarMaxRangeDays - 1)).IsSuccess.ShouldBeTrue();
        var tooLarge = LeaveCalendar.ValidateRange(Start, Start.AddDays(LeaveLimits.CalendarMaxRangeDays));
        tooLarge.Error.Code.ShouldBe(LeaveErrors.CalendarRangeTooLarge);
        tooLarge.Error.Type.ShouldBe(ErrorType.Validation);
        LeaveCalendar.ValidateRange(Start, Start).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Calendar_MasksSensitiveAndHiddenTypes_AndSortsByStartThenName()
    {
        var set = LeaveRuleSet.Create(Guid.NewGuid(), "Kural").Value;
        var annual = set.AddType(new LeaveTypeSettings(LeaveTypeCode.Annual, "leave.type.annual", null, true, LeaveLimitType.PerYear, AnnualDays: 14m)).Value;
        var sick = set.AddType(new LeaveTypeSettings(LeaveTypeCode.Sick, "leave.type.sick", null, true, LeaveLimitType.Unlimited, IsSensitive: true)).Value;
        var hidden = set.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "Gizli", null, true, LeaveLimitType.Unlimited, ShowTypeNameOnCalendar: false)).Value;
        var types = set.Types.ToDictionary(t => t.Id);

        var ayse = Guid.NewGuid();
        var berk = Guid.NewGuid();
        var cem = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [ayse] = "Ayşe Yılmaz", [berk] = "Berk Demir", [cem] = "Cem Kaya" };
        var entries = LeaveCalendar.Build(
            [
                NewRequest(cem, annual.Id, Start), NewRequest(berk, sick.Id, Start), NewRequest(ayse, hidden.Id, Start.AddDays(1)),
                NewRequest(ayse, Guid.NewGuid(), Start.AddDays(2)),
            ],
            types,
            names);

        entries.Select(e => e.EmployeeName).ShouldBe(["Berk Demir", "Cem Kaya", "Ayşe Yılmaz", "Ayşe Yılmaz"]);
        entries[0].IsMasked.ShouldBeTrue();
        entries[0].LeaveTypeCode.ShouldBeNull();
        entries[0].LeaveTypeName.ShouldBeNull();
        entries[1].IsMasked.ShouldBeFalse();
        entries[1].LeaveTypeCode.ShouldBe(LeaveTypeCode.Annual);
        entries[1].LeaveTypeName.ShouldBe("leave.type.annual");
        entries[1].Status.ShouldBe(LeaveRequestStatus.Pending);
        entries[2].IsMasked.ShouldBeTrue();
        entries[2].LeaveTypeName.ShouldBeNull();
        entries[3].IsMasked.ShouldBeTrue(); // type of a deleted rule set
    }

    [Fact]
    public void RuleSet_FindCounterpart_MatchesByCode_AndCustomByName()
    {
        var tenantId = Guid.NewGuid();
        var from = LeaveRuleSet.Create(tenantId, "A").Value;
        var to = LeaveRuleSet.Create(tenantId, "B").Value;
        var annualFrom = from.AddType(new LeaveTypeSettings(LeaveTypeCode.Annual, "Yıllık", null, true, LeaveLimitType.PerYear, AnnualDays: 14m)).Value;
        var customFrom = from.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "Kurum izni", null, true, LeaveLimitType.Unlimited)).Value;
        var excuseFrom = from.AddType(new LeaveTypeSettings(LeaveTypeCode.Excuse, "Mazeret", null, true, LeaveLimitType.Unlimited)).Value;
        var annualTo = to.AddType(new LeaveTypeSettings(LeaveTypeCode.Annual, "Yıllık izin", null, true, LeaveLimitType.PerYear, AnnualDays: 20m)).Value;
        to.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "Başka", null, true, LeaveLimitType.Unlimited));
        var customTo = to.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "KURUM IZNI", null, true, LeaveLimitType.Unlimited)).Value;

        to.FindCounterpart(annualFrom).ShouldBe(annualTo);
        to.FindCounterpart(excuseFrom).ShouldBeNull();
        to.FindCounterpart(customFrom).ShouldBe(customTo);
    }
}

/// <summary>Cancel, resubmission and rule set management handlers on real PostgreSQL (Workflow/data scope substituted).</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveManagementIntegrationTests : TestFixture
{
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();

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

    private async Task<Result<T>> RunAsync<THandler, T>(Func<THandler, Task<Result<T>>> run, params object[] args)
        where THandler : notnull
    {
        using var scope = ServiceProvider!.CreateScope();
        var result = await run(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider, args));
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    private async Task<Result> RunAsync<THandler>(Func<THandler, Task<Result>> run, params object[] args)
        where THandler : notnull
    {
        using var scope = ServiceProvider!.CreateScope();
        var result = await run(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider, args));
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    private static DateOnly NextWeekMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
    }

    private async Task<(Guid TenantId, Guid EmployeeId, Guid AnnualId)> HireAsync()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var accrualStart = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1).AddDays(-10);
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new EmployeeHired(tenantId, employeeId, accrualStart, accrualStart));
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var read = ServiceProvider.CreateScope();
        var annualId = (await read.ServiceProvider.GetRequiredService<LeaveDbContext>().RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
        return (tenantId, employeeId, annualId);
    }

    [Fact]
    public async Task CancelApproved_PostsReversal_RestoresBalance_AndPublishesLeaveCancelled()
    {
        var (tenantId, employeeId, annualId) = await HireAsync();
        _currentUser.EmployeeId.Returns(employeeId);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), employeeId, Arg.Any<CancellationToken>()).Returns(true);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var created = await RunAsync<CreateLeaveRequestOnBehalfHandler, LeaveRequestDto>(
            h => h.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(1), null, true), CancellationToken.None),
            _currentUser, _dataScope, _workflow);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
        var createdId = created.Value.Id;

        (await RunAsync<CancelLeaveRequestHandler>(
            h => h.Handle(new CancelLeaveRequestCommand(Guid.NewGuid(), createdId, "yanlış kayıt"), CancellationToken.None), _currentUser, _dataScope, _workflow))
            .Error.Type.ShouldBe(ErrorType.Forbidden);

        var cancelled = await RunAsync<CancelLeaveRequestHandler>(
            h => h.Handle(new CancelLeaveRequestCommand(employeeId, createdId, "yanlış kayıt"), CancellationToken.None), _currentUser, _dataScope, _workflow);
        cancelled.IsSuccess.ShouldBeTrue(cancelled.IsFailure ? cancelled.Error.Code : null);
        await _workflow.DidNotReceiveWithAnyArgs().CancelAsync(default, default, default);

        using (var verify = ServiceProvider!.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await db.Requests.SingleAsync(r => r.Id == createdId);
            request.Status.ShouldBe(LeaveRequestStatus.Cancelled);
            request.CancellationReason.ShouldBe("yanlış kayıt");
            request.CancelledByUserId.ShouldBe(_currentUser.UserId);
            request.CancelledAt.ShouldNotBeNull();

            var reversal = await db.LedgerEntries.SingleAsync(e => e.Type == LeaveLedgerEntryType.Reversal);
            reversal.Amount.ShouldBe(2m);
            reversal.EffectiveDate.ShouldBe(monday);
            reversal.SourceRefId.ShouldBe(createdId);

            var profile = await db.Profiles.SingleAsync();
            var set = await db.RuleSets.SingleAsync();
            var balance = (await verify.ServiceProvider.GetRequiredService<LeaveBalanceService>().ProjectAsync(profile, set, CancellationToken.None))
                .Single(b => b.LeaveTypeId == annualId);
            balance.FutureApprovedInPeriod.ShouldBe(0m);
            balance.AvailableBalance.ShouldBe(balance.CurrentBalance);

            var outbox = await db.OutboxMessages.Where(m => m.TenantId == tenantId).Select(m => m.Type).ToListAsync();
            outbox.ShouldContain(t => t.EndsWith(nameof(Contracts.Events.LeaveCancelled), StringComparison.Ordinal));

            var detail = await ActivatorUtilities.CreateInstance<GetMyLeaveRequestHandler>(verify.ServiceProvider, _currentUser, SubstituteDetails(verify.ServiceProvider))
                .Handle(new GetMyLeaveRequestQuery(createdId), CancellationToken.None);
            detail.Value.Status.ShouldBe(LeaveRequestStatus.Cancelled);
            detail.Value.CancellationReason.ShouldBe("yanlış kayıt");
            detail.Value.EmployeeName.ShouldBe("Ada Lovelace");
            detail.Value.LeaveTypeName.ShouldBe("leave.type.annual");
            detail.Value.Approval.ShouldBeNull();
        }

        (await RunAsync<CancelLeaveRequestHandler>(
            h => h.Handle(new CancelLeaveRequestCommand(employeeId, createdId, "tekrar"), CancellationToken.None), _currentUser, _dataScope, _workflow))
            .Error.Code.ShouldBe(LeaveDomainErrors.RequestNotCancellable);
    }

    [Fact]
    public async Task CancelPending_CancelsWorkflowApproval_WithoutLedgerRows()
    {
        var (tenantId, employeeId, annualId) = await HireAsync();
        _currentUser.EmployeeId.Returns(employeeId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        _workflow.CancelAsync(approvalId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var submitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null), CancellationToken.None), _currentUser, _workflow);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
        var submittedId = submitted.Value.Id;

        var cancelled = await RunAsync<CancelLeaveRequestHandler>(
            h => h.Handle(new CancelLeaveRequestCommand(employeeId, submittedId, "vazgeçildi"), CancellationToken.None), _currentUser, _dataScope, _workflow);
        cancelled.IsSuccess.ShouldBeTrue(cancelled.IsFailure ? cancelled.Error.Code : null);
        await _workflow.Received(1).CancelAsync(approvalId, employeeId, Arg.Any<CancellationToken>());

        using var verify = ServiceProvider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
        (await db.Requests.SingleAsync(r => r.Id == submittedId)).Status.ShouldBe(LeaveRequestStatus.Cancelled);
        (await db.LedgerEntries.CountAsync(e => e.Type == LeaveLedgerEntryType.Reversal || e.Type == LeaveLedgerEntryType.Usage)).ShouldBe(0);

        // A late Workflow decision for the cancelled request is ignored.
        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Leave, submittedId, employeeId));
        using var after = ServiceProvider.CreateScope();
        (await after.ServiceProvider.GetRequiredService<LeaveDbContext>().Requests.SingleAsync(r => r.Id == submittedId)).Status.ShouldBe(LeaveRequestStatus.Cancelled);
    }

    [Fact]
    public async Task Detail_TeamOverlap_ResolvesOrgUnitName()
    {
        // senseik-026: the leave request detail's team-overlap panel becomes a day-by-day calendar grid with a
        // group heading - the heading needs the requester's org unit name, best-effort resolved via IOrgUnitLookup.
        var (tenantId, employeeId, annualId) = await HireAsync();
        _currentUser.EmployeeId.Returns(employeeId);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var created = await RunAsync<CreateLeaveRequestOnBehalfHandler, LeaveRequestDto>(
            h => h.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(1), null, true), CancellationToken.None),
            _currentUser, _dataScope, _workflow);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);

        var orgUnitId = Guid.NewGuid();
        using var verify = ServiceProvider!.CreateScope();
        var detail = await ActivatorUtilities.CreateInstance<GetMyLeaveRequestHandler>(
                verify.ServiceProvider, _currentUser, SubstituteDetails(verify.ServiceProvider, (orgUnitId, "Human Resources")))
            .Handle(new GetMyLeaveRequestQuery(created.Value.Id), CancellationToken.None);

        detail.IsSuccess.ShouldBeTrue(detail.IsFailure ? detail.Error.Code : null);
        detail.Value.TeamOverlap.ShouldNotBeNull();
        detail.Value.TeamOverlap!.OrgUnitName.ShouldBe("Human Resources");
        detail.Value.TeamOverlap.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Resubmission_RequiresOwnRejectedRequest()
    {
        var (tenantId, employeeId, annualId) = await HireAsync();
        _currentUser.EmployeeId.Returns(employeeId);
        var firstApproval = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Leave, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(firstApproval), Result.Success(Guid.NewGuid()), Result.Success(Guid.NewGuid()));
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        var original = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null), CancellationToken.None), _currentUser, _workflow);
        original.IsSuccess.ShouldBeTrue(original.IsFailure ? original.Error.Code : null);
        var originalId = original.Value.Id;

        // Still pending → not a valid resubmission source; an unknown id neither.
        (await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday.AddDays(7), monday.AddDays(7), null, originalId), CancellationToken.None), _currentUser, _workflow))
            .Error.Code.ShouldBe(LeaveErrors.ResubmissionSourceInvalid);
        (await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday.AddDays(7), monday.AddDays(7), null, Guid.NewGuid()), CancellationToken.None), _currentUser, _workflow))
            .Error.Type.ShouldBe(ErrorType.Conflict);

        await ServiceProvider!.GetRequiredService<IEventBus>().Publish(new ApprovalRejected(tenantId, firstApproval, ApprovalRequestTypes.Leave, originalId, employeeId, 1));

        // Someone else's rejected request cannot be resubmitted.
        var stranger = Substitute.For<ICurrentUser>();
        stranger.EmployeeId.Returns(Guid.NewGuid());
        (await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday, null, originalId), CancellationToken.None), stranger, _workflow))
            .Error.Code.ShouldBe(LeaveErrors.ResubmissionSourceInvalid);

        var resubmitted = await RunAsync<SubmitLeaveRequestHandler, LeaveRequestDto>(
            h => h.Handle(new SubmitLeaveRequestCommand(annualId, monday, monday.AddDays(1), "düzeltildi", originalId), CancellationToken.None), _currentUser, _workflow);
        resubmitted.IsSuccess.ShouldBeTrue(resubmitted.IsFailure ? resubmitted.Error.Code : null);

        using var verify = ServiceProvider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var stored = await db.Requests.SingleAsync(r => r.Id == resubmitted.Value.Id);
        stored.Source.ShouldBe(LeaveRequestSource.Resubmission);
        stored.PreviousRequestId.ShouldBe(originalId);
        stored.Status.ShouldBe(LeaveRequestStatus.Pending);
        (await db.Requests.SingleAsync(r => r.Id == originalId)).Status.ShouldBe(LeaveRequestStatus.Rejected);
    }

    [Fact]
    public async Task RuleSets_SetDefault_Delete_AndAssignWithRecalculationAndWarnings()
    {
        var (tenantId, employeeId, annualId) = await HireAsync();
        _currentUser.EmployeeId.Returns(employeeId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), employeeId, Arg.Any<CancellationToken>()).Returns(true);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var monday = NextWeekMonday();

        // Approved usage on the seeded Annual type.
        (await RunAsync<CreateLeaveRequestOnBehalfHandler, LeaveRequestDto>(
            h => h.Handle(new CreateLeaveRequestOnBehalfCommand(employeeId, annualId, monday, monday.AddDays(2), null, true), CancellationToken.None),
            _currentUser, _dataScope, _workflow)).IsSuccess.ShouldBeTrue();

        Guid seedId;
        Guid cloneId;
        Guid customId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var seed = await db.RuleSets.SingleAsync();
            seedId = seed.Id;
            var clone = seed.Clone("Kopya").Value;
            var custom = LeaveRuleSet.Create(tenantId, "Yalnız uzaktan çalışma").Value;
            custom.AddType(new LeaveTypeSettings(LeaveTypeCode.RemoteWork, "Uzaktan", null, true, LeaveLimitType.Unlimited)).IsSuccess.ShouldBeTrue();
            db.RuleSets.AddRange(clone, custom);
            await db.SaveChangesAsync();
            cloneId = clone.Id;
            customId = custom.Id;
        }

        (await RunAsync<RenameLeaveRuleSetHandler>(h => h.Handle(new RenameLeaveRuleSetCommand(cloneId, "Yeni kural"), CancellationToken.None))).IsSuccess.ShouldBeTrue();
        (await RunAsync<SetDefaultLeaveRuleSetHandler>(h => h.Handle(new SetDefaultLeaveRuleSetCommand(cloneId), CancellationToken.None))).IsSuccess.ShouldBeTrue();
        (await RunAsync<DeleteLeaveRuleSetHandler>(h => h.Handle(new DeleteLeaveRuleSetCommand(cloneId), CancellationToken.None)))
            .Error.Code.ShouldBe(LeaveDomainErrors.RuleSetIsDefault);
        (await RunAsync<DeleteLeaveRuleSetHandler>(h => h.Handle(new DeleteLeaveRuleSetCommand(seedId), CancellationToken.None)))
            .Error.Code.ShouldBe(LeaveDomainErrors.RuleSetHasEmployees);

        // Scope check covers every employee.
        (await RunAsync<AssignLeaveRuleSetHandler, LeaveRuleSetAssignmentResult>(
            h => h.Handle(new AssignLeaveRuleSetCommand(cloneId, [employeeId, Guid.NewGuid()]), CancellationToken.None), _dataScope))
            .Error.Type.ShouldBe(ErrorType.Forbidden);

        var assigned = await RunAsync<AssignLeaveRuleSetHandler, LeaveRuleSetAssignmentResult>(
            h => h.Handle(new AssignLeaveRuleSetCommand(cloneId, [employeeId, employeeId]), CancellationToken.None), _dataScope);
        assigned.IsSuccess.ShouldBeTrue(assigned.IsFailure ? assigned.Error.Code : null);
        assigned.Value.AssignedCount.ShouldBe(1);
        assigned.Value.Warnings.ShouldBeEmpty();

        using (var verify = ServiceProvider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var sets = await db.RuleSets.ToListAsync();
            sets.Count(s => s.IsDefault).ShouldBe(1);
            sets.Single(s => s.Id == cloneId).Name.ShouldBe("Yeni kural");
            var cloneAnnual = sets.Single(s => s.Id == cloneId).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
            (await db.Profiles.SingleAsync()).RuleSetId.ShouldBe(cloneId);
            (await db.LedgerEntries.CountAsync(e => e.LeaveTypeId == annualId)).ShouldBe(0);
            (await db.LedgerEntries.SingleAsync(e => e.Type == LeaveLedgerEntryType.Usage)).LeaveTypeId.ShouldBe(cloneAnnual);
            (await db.LedgerEntries.CountAsync(e => e.Type == LeaveLedgerEntryType.Accrual && e.LeaveTypeId == cloneAnnual)).ShouldBe(1);
            (await db.Requests.SingleAsync()).LeaveTypeId.ShouldBe(cloneAnnual);
        }

        // Idempotent re-assign; then a set without an Annual type → warning, usage stays on the old type.
        (await RunAsync<AssignLeaveRuleSetHandler, LeaveRuleSetAssignmentResult>(
            h => h.Handle(new AssignLeaveRuleSetCommand(cloneId, [employeeId]), CancellationToken.None), _dataScope)).Value.AssignedCount.ShouldBe(1);
        var moved = await RunAsync<AssignLeaveRuleSetHandler, LeaveRuleSetAssignmentResult>(
            h => h.Handle(new AssignLeaveRuleSetCommand(customId, [employeeId]), CancellationToken.None), _dataScope);
        moved.IsSuccess.ShouldBeTrue(moved.IsFailure ? moved.Error.Code : null);
        var warning = moved.Value.Warnings.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LeaveWarnings.UsageTypeMissingInRuleSet);
        warning.EmployeeId.ShouldBe(employeeId);
        warning.LeaveTypeName.ShouldBe("leave.type.annual");

        (await RunAsync<DeleteLeaveRuleSetHandler>(h => h.Handle(new DeleteLeaveRuleSetCommand(seedId), CancellationToken.None))).IsSuccess.ShouldBeTrue();
        using var final = ServiceProvider.CreateScope();
        var finalDb = final.ServiceProvider.GetRequiredService<LeaveDbContext>();
        (await finalDb.RuleSets.CountAsync()).ShouldBe(2);
        (await finalDb.LedgerEntries.CountAsync(e => e.Type == LeaveLedgerEntryType.Accrual)).ShouldBe(0);
        (await finalDb.LedgerEntries.CountAsync(e => e.Type == LeaveLedgerEntryType.Usage)).ShouldBe(1);
    }

    /// <summary>
    /// Unconfigured <paramref name="orgUnit"/>: GetOrgUnitIdAsync returns null (NSubstitute default), so
    /// ResolveTeamOverlapAsync short-circuits to a null overlap. Pass <paramref name="orgUnit"/> to exercise the
    /// org-unit-name resolution path (senseik-026 calendar grid header).
    /// </summary>
    private static LeaveRequestDetailBuilder SubstituteDetails(IServiceProvider services, (Guid Id, string Name)? orgUnit = null)
    {
        var directory = Substitute.For<Sense.Modules.Employee.Contracts.IEmployeeDirectory>();
        directory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<IReadOnlyCollection<Guid>>(1).Select(id => new Sense.Modules.Employee.Contracts.EmployeeContact(id, "Ada", "Lovelace", null, null, null, "Active")).ToList());
        var employeeLookup = Substitute.For<Sense.Modules.Employee.Contracts.IEmployeeLookup>();
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        if (orgUnit is { } unit)
        {
            employeeLookup.GetOrgUnitIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(unit.Id);
            orgUnits.ListActiveUnitsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns([new OrgUnitInfo(unit.Id, Guid.NewGuid(), null, unit.Name)]);
            // No peers configured (senseik-026 test only asserts OrgUnitName resolution) - ResolveTeamOverlapAsync
            // still returns a non-null TeamOverlapDto with Count 0 once GetOrgUnitIdAsync is non-null.
            directory.ListActiveEmployeeIdsInOrgUnitsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<Guid>)[]);
        }

        return ActivatorUtilities.CreateInstance<LeaveRequestDetailBuilder>(
            services, directory, employeeLookup, Substitute.For<IApprovalRequestLookup>(), orgUnits);
    }
}
