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
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Jobs;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class OvertimeDomainTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private static IReadOnlyList<ScheduledInterval> NineToSix(params DateOnly[] days) =>
        days.Select(d => new ScheduledInterval(d.ToDateTime(new TimeOnly(9, 0)), d.ToDateTime(new TimeOnly(18, 0)))).ToList();

    [Theory]
    [InlineData(17, 0, 180, 60)] // 17:00–20:00 → 1 h inside 09–18
    [InlineData(18, 0, 120, 0)] // after hours
    [InlineData(8, 0, 120, 60)] // 08:00–10:00
    [InlineData(10, 0, 60, 60)] // fully inside
    [InlineData(7, 0, 12 * 60, 9 * 60)] // 07:00–19:00 covers the whole schedule
    public void OverlapMinutes_ClipsToPlannedHours(int hour, int minute, int duration, int expected)
    {
        OvertimeMath.OverlapMinutes(Monday.ToDateTime(new TimeOnly(hour, minute)), duration, NineToSix(Monday)).ShouldBe(expected);
    }

    [Fact]
    public void OverlapMinutes_MergesOverlappingIntervals_AndCrossesMidnight()
    {
        var tuesday = Monday.AddDays(1);
        var intervals = NineToSix(Monday, tuesday).Append(new ScheduledInterval(Monday.ToDateTime(new TimeOnly(17, 0)), Monday.ToDateTime(new TimeOnly(19, 0)))).ToList();

        // Mon 16:00 → Tue 10:00: Mon 16–19 (merged 3 h) + Tue 09–10 (1 h).
        OvertimeMath.OverlapMinutes(Monday.ToDateTime(new TimeOnly(16, 0)), 18 * 60, intervals).ShouldBe(240);
    }

    [Fact]
    public void Submit_PayableMinutes_IsDurationMinusOverlap()
    {
        var request = OvertimeRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Monday, new TimeOnly(17, 0), 180, OvertimeDayKind.Workday, 60, null,
            OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value;

        request.PayableMinutes.ShouldBe(120);
        request.EndAt.ShouldBe(Monday.ToDateTime(new TimeOnly(20, 0)));
        OvertimeRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Monday, new TimeOnly(17, 0), 30, OvertimeDayKind.Workday, 60, null,
            OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Error.Code.ShouldBe(LeaveDomainErrors.OvertimeInvalidOverlap);
        OvertimeRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Monday, new TimeOnly(17, 0), 0, OvertimeDayKind.Workday, 0, null,
            OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Error.Code.ShouldBe(LeaveDomainErrors.OvertimeInvalidDuration);
    }

    [Fact]
    public void ConsentRevocation_TakesEffectAfterNoticeDays()
    {
        var consent = OvertimeConsent.Record(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2025, 1, 1), OvertimeConsentSource.EmploymentContract, false, null, null);
        var notice = new DateOnly(2026, 9, 1);

        consent.Revoke(notice, 30).IsSuccess.ShouldBeTrue();
        consent.RevocationEffectiveAt.ShouldBe(new DateOnly(2026, 10, 1));
        consent.Revoke(notice, 30).Error.Code.ShouldBe(LeaveDomainErrors.OvertimeConsentAlreadyRevoked);

        OvertimeMath.ConsentState([consent], new DateOnly(2026, 9, 30), out _).ShouldBe(OvertimeConsentState.InEffect);
        OvertimeMath.ConsentState([consent], new DateOnly(2026, 10, 1), out _).ShouldBe(OvertimeConsentState.Revoked);
        OvertimeMath.ConsentState([], new DateOnly(2026, 10, 1), out _).ShouldBe(OvertimeConsentState.Missing);
        OvertimeConsent.Record(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 2), OvertimeConsentSource.StandaloneForm, true, null, null)
            .Revoke(new DateOnly(2026, 9, 1), 30).Error.Code.ShouldBe(LeaveDomainErrors.OvertimeConsentInvalidRevocation);
    }

    [Fact]
    public void TouchesNight_DetectsTwentyToSix()
    {
        OvertimeMath.TouchesNight(Monday.ToDateTime(new TimeOnly(18, 0)), 120).ShouldBeFalse();
        OvertimeMath.TouchesNight(Monday.ToDateTime(new TimeOnly(19, 0)), 90).ShouldBeTrue();
        OvertimeMath.TouchesNight(Monday.ToDateTime(new TimeOnly(5, 0)), 60).ShouldBeTrue();
    }

    [Fact]
    public void ConvertToTimeOff_UsesPayableOrFullBasis_AndRate()
    {
        var request = OvertimeRequest.Submit(Guid.NewGuid(), Guid.NewGuid(), Monday, new TimeOnly(17, 0), 180, OvertimeDayKind.Workday, 60, null,
            OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value;
        var approval = Guid.NewGuid();
        request.AttachApproval(approval);

        OvertimeConversion.ToTimeOff(request, OvertimeMinutesBasis.Payable, 1.5m, 450, Guid.NewGuid(), null, DateTime.UtcNow, null).Error.Code
            .ShouldBe(LeaveDomainErrors.OvertimeNotApproved);
        request.Approve(approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        // Payable 120 min × 1.5 = 180 min = 0.4 day of 7.5 h; full 180 × 1.5 = 270 min = 0.6 day.
        var payable = OvertimeConversion.ToTimeOff(request, OvertimeMinutesBasis.Payable, 1.5m, 450, Guid.NewGuid(), null, DateTime.UtcNow, null).Value;
        payable.ResultMinutes.ShouldBe(180);
        payable.ResultDays.ShouldBe(0.4m);
        var full = OvertimeConversion.ToTimeOff(request, OvertimeMinutesBasis.Full, 1.5m, 450, Guid.NewGuid(), null, DateTime.UtcNow, null).Value;
        full.ResultMinutes.ShouldBe(270);
        full.ResultDays.ShouldBe(0.6m);
        OvertimeMath.TimeOff(100, 1.25m, 450).ShouldBe((125, 0.2778m));

        request.MarkConverted(payable.Id).IsSuccess.ShouldBeTrue();
        request.MarkConverted(full.Id).Error.Code.ShouldBe(LeaveDomainErrors.OvertimeAlreadyConverted);
        OvertimeConversion.ToTimeOff(request, OvertimeMinutesBasis.Payable, 1.5m, 450, Guid.NewGuid(), null, DateTime.UtcNow, null).Error.Code
            .ShouldBe(LeaveDomainErrors.OvertimeAlreadyConverted);
    }

    /// <summary>Kart senseik-030: "Kind=Pay" - no ledger math, snapshots the request's own %25/%50 pay-rate multiplier, still blocks a second conversion.</summary>
    [Fact]
    public void ConvertToPay_SnapshotsRequestRate_AndBlocksSecondConversion()
    {
        var request = OvertimeRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), Monday, new TimeOnly(17, 0), 180, OvertimeDayKind.Workday, 60, null,
            OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, OvertimePayRateKind.Rate25).Value;

        OvertimeConversion.ToPay(request, OvertimeMinutesBasis.Payable, null, DateTime.UtcNow, null).Error.Code
            .ShouldBe(LeaveDomainErrors.OvertimeNotApproved);

        var approval = Guid.NewGuid();
        request.AttachApproval(approval);
        request.Approve(approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var payable = OvertimeConversion.ToPay(request, OvertimeMinutesBasis.Payable, null, DateTime.UtcNow, "nakde çevir").Value;
        payable.Kind.ShouldBe(OvertimeConversionKind.Pay);
        payable.BasisMinutes.ShouldBe(120); // PayableMinutes (180 duration − 60 overlap)
        payable.ResultMinutes.ShouldBe(120); // Pay does not transform minutes - the amount is Payroll's own math.
        payable.ResultDays.ShouldBe(0m);
        payable.RateSnapshot.ShouldBe(1.25m); // request.PayRateMultiplier snapshot (Rate25), not a conversion-specific rate.
        payable.LeaveTypeId.ShouldBeNull();
        payable.LedgerEntryId.ShouldBeNull();
        payable.UsedByDate.ShouldBeNull();

        request.MarkConverted(payable.Id).IsSuccess.ShouldBeTrue();
        OvertimeConversion.ToPay(request, OvertimeMinutesBasis.Payable, null, DateTime.UtcNow, null).Error.Code
            .ShouldBe(LeaveDomainErrors.OvertimeAlreadyConverted);
    }
}

/// <summary>Overtime submit → Workflow "Overtime" approval (substituted) → ApprovalCompleted via IEventBus → conversion posts an ExtraGrant row.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class OvertimeIntegrationTests : TestFixture
{
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();

    /// <summary>The submit handlers reject work dates after tenant-local today; the scenarios below use fixed 2026-09/10 dates, so "now" is pinned after all of them.</summary>
    private static readonly TimeProvider AfterAllTestDates = new PinnedTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    private sealed class PinnedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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
        // Zaman-devam: Attendance'ın ApprovalCompleted işleyicisi (eksik kayıt talebi) IPunchRecorder -> ITenantLocalClock üzerinden kiracı
        // saat dilimini ister; Api/Worker'da Identity kaydeder. Dilimsiz kiracı = UTC.
        services.AddSingleton(Substitute.For<Sense.Modules.Identity.Contracts.ITenantTimeZoneLookup>());
        services.AddLeaveServices(configuration);
        // Denetim A5-belge: LeaveRequestRules'un Documents bağı (bu dosyadaki senaryolar belge kuralı dışı).
        services.AddSingleton(DocumentLookupDoubles.Permissive());
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly, typeof(ILeaveRuleSetRepository).Assembly, typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);

        var holidays = Substitute.For<IHolidayCalendarLookup>();
        holidays.ResolveCalendarAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((HolidayCalendarRef?)null);
        services.AddSingleton(holidays);

        // senseik-007 (age-50-exception): EmployeeHiredHandler now needs IEmployeeBirthDateLookup - without it, DI
        // validation fails for every test in this fixture (WorkerRegistration_ResolvesEveryLeaveIntegrationEventHandler
        // and beyond), same pattern as LeaveBalanceRecalculationJobTests.
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
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider, args);
        var result = await run(handler);
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    [Fact]
    public async Task Submit_Approve_Convert_PostsExtraGrant_AndConsentRevocationBlocks()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var hire = new DateOnly(2024, 1, 1);
        _currentUser.EmployeeId.Returns(employeeId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesOverridePayable.Key, employeeId, Arg.Any<CancellationToken>()).Returns(false);
        var approvalId = Guid.NewGuid();
        _workflow.StartAsync(ApprovalRequestTypes.Overtime, Arg.Any<Guid>(), employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(approvalId));
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        await bus.Publish(new EmployeeHired(tenantId, employeeId, hire, hire));
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        // Monday 2026-09-14 17:00 for 3 h: 1 h inside the placeholder 09–18 schedule; no consent yet → warnings, still submitted.
        var monday = new DateOnly(2026, 9, 14);
        var submitted = await RunAsync<SubmitMyOvertimeRequestHandler, OvertimeSubmissionDto>(
            h => h.Handle(new SubmitMyOvertimeRequestCommand(monday, new TimeOnly(17, 0), 180, "sayım"), CancellationToken.None), _currentUser, _workflow, AfterAllTestDates);
        submitted.IsSuccess.ShouldBeTrue(submitted.IsFailure ? submitted.Error.Code : null);
        submitted.Value.PayableMinutes.ShouldBe(120);
        submitted.Value.Warnings.ShouldContain(OvertimeWarnings.ConsentMissing);
        submitted.Value.Warnings.ShouldContain(OvertimeWarnings.OverlapsSchedule);

        (await RunAsync<SubmitMyOvertimeRequestHandler, OvertimeSubmissionDto>(
            h => h.Handle(new SubmitMyOvertimeRequestCommand(monday, new TimeOnly(19, 0), 60, null), CancellationToken.None), _currentUser, _workflow, AfterAllTestDates))
            .Error.Code.ShouldBe(OvertimeErrors.Overlap);

        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Overtime, submitted.Value.Id, employeeId));

        (await RunAsync<ConvertOvertimeToTimeOffHandler, OvertimeConversionDto>(
            h => h.Handle(new ConvertOvertimeToTimeOffCommand(employeeId, submitted.Value.Id, null, OvertimeMinutesBasis.Full, null, null), CancellationToken.None),
            _currentUser, _dataScope)).Error.Code.ShouldBe(LeaveErrors.OverridePayableNotAllowed);

        var converted = await RunAsync<ConvertOvertimeToTimeOffHandler, OvertimeConversionDto>(
            h => h.Handle(new ConvertOvertimeToTimeOffCommand(employeeId, submitted.Value.Id, null, OvertimeMinutesBasis.Payable, null, "serbest zaman"), CancellationToken.None),
            _currentUser, _dataScope);
        converted.IsSuccess.ShouldBeTrue(converted.IsFailure ? converted.Error.Code : null);
        converted.Value.ResultMinutes.ShouldBe(180);
        converted.Value.ResultDays.ShouldBe(0.4m);

        using (var verify = ServiceProvider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await db.OvertimeRequests.SingleAsync(r => r.Id == submitted.Value.Id);
            request.Status.ShouldBe(OvertimeRequestStatus.Approved);
            request.ConversionId.ShouldBe(converted.Value.Id);
            var grant = await db.LedgerEntries.SingleAsync(e => e.Type == LeaveLedgerEntryType.ExtraGrant);
            grant.Amount.ShouldBe(0.4m);
            grant.Source.ShouldBe(LeaveLedgerSource.OvertimeConversion);
            grant.SourceRefId.ShouldBe(converted.Value.Id);
            grant.LeaveTypeId.ShouldBe((await db.RuleSets.SingleAsync()).Types.Single(t => t.Code == LeaveTypeCode.Annual).Id);
            var outboxTypes = await db.OutboxMessages.Where(m => m.TenantId == tenantId).Select(m => m.Type).ToListAsync();
            outboxTypes.ShouldContain(t => t.EndsWith(nameof(Contracts.Events.OvertimeRequestSubmitted), StringComparison.Ordinal));
            outboxTypes.ShouldContain(t => t.EndsWith(nameof(Contracts.Events.OvertimeApproved), StringComparison.Ordinal));
            outboxTypes.ShouldContain(t => t.EndsWith(nameof(Contracts.Events.OvertimeConvertedToTimeOff), StringComparison.Ordinal));
        }

        (await RunAsync<ConvertOvertimeToTimeOffHandler, OvertimeConversionDto>(
            h => h.Handle(new ConvertOvertimeToTimeOffCommand(employeeId, submitted.Value.Id, null, OvertimeMinutesBasis.Payable, null, null), CancellationToken.None),
            _currentUser, _dataScope)).Error.Code.ShouldBe(LeaveDomainErrors.OvertimeAlreadyConverted);

        // Consent given, then revoked with notice on 2026-09-01 → effective 2026-10-01 (30 days, legislation parameter).
        var consentId = await RunAsync<RecordOvertimeConsentHandler, Guid>(
            h => h.Handle(new RecordOvertimeConsentCommand(employeeId, hire, OvertimeConsentSource.EmploymentContract, false, null), CancellationToken.None),
            _currentUser, _dataScope);
        var revoked = await RunAsync<RevokeOvertimeConsentHandler, OvertimeConsentDto>(
            h => h.Handle(new RevokeOvertimeConsentCommand(employeeId, consentId.Value, new DateOnly(2026, 9, 1)), CancellationToken.None), _dataScope);
        revoked.Value.RevocationEffectiveAt.ShouldBe(new DateOnly(2026, 10, 1));

        var inNotice = await RunAsync<SubmitMyOvertimeRequestHandler, OvertimeSubmissionDto>(
            h => h.Handle(new SubmitMyOvertimeRequestCommand(new DateOnly(2026, 9, 26), new TimeOnly(10, 0), 60, null), CancellationToken.None), _currentUser, _workflow, AfterAllTestDates);
        inNotice.IsSuccess.ShouldBeTrue(inNotice.IsFailure ? inNotice.Error.Code : null);
        // Saturday, consent still in effect during notice. The pinned clock is 14 days after the work date, so the
        // (non-blocking) past-date warning is expected; only the consent warnings must be absent.
        inNotice.Value.Warnings.ShouldNotContain(OvertimeWarnings.ConsentMissing);
        inNotice.Value.Warnings.ShouldNotContain(OvertimeWarnings.NightConsentMissing);
        (await RunAsync<SubmitMyOvertimeRequestHandler, OvertimeSubmissionDto>(
            h => h.Handle(new SubmitMyOvertimeRequestCommand(new DateOnly(2026, 10, 3), new TimeOnly(10, 0), 60, null), CancellationToken.None), _currentUser, _workflow, AfterAllTestDates))
            .Error.Code.ShouldBe(OvertimeErrors.ConsentRevoked);
    }

    [Fact]
    public async Task BalanceRecalculationJob_RegeneratesAccruals_ForEveryTenant()
    {
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();
        var hire = new DateOnly(2023, 3, 1);
        var tenants = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var tenantId in tenants)
        {
            await bus.Publish(new EmployeeHired(tenantId, Guid.NewGuid(), hire, hire));
        }

        using (var wipe = ServiceProvider.CreateScope())
        {
            await wipe.ServiceProvider.GetRequiredService<LeaveDbContext>().LedgerEntries.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
                .Where(e => tenants.Contains(e.TenantId)).ExecuteDeleteAsync();
        }

        var job = ActivatorUtilities.CreateInstance<LeaveBalanceRecalculationJob>(ServiceProvider);
        (await job.RunAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(2);

        using var verify = ServiceProvider.CreateScope();
        var accruals = await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().LedgerEntries.IgnoreQueryFilters([ModuleDbContext.TenantFilter])
            .Where(e => tenants.Contains(e.TenantId) && e.Type == LeaveLedgerEntryType.Accrual)
            .Select(e => e.TenantId).Distinct().CountAsync();
        accruals.ShouldBe(2);
        LeaveBalanceRecalculationHostedService.DelayUntilNextRun(new DateTimeOffset(2026, 9, 15, 0, 30, 0, TimeSpan.Zero)).ShouldBe(TimeSpan.FromMinutes(30));
        LeaveBalanceRecalculationHostedService.DelayUntilNextRun(new DateTimeOffset(2026, 9, 15, 1, 0, 0, TimeSpan.Zero)).ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task ApprovalRejected_ForOvertime_MarksRejected_AndLeaveTypeEventsAreIgnored()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        Guid requestId;
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = OvertimeRequest.Submit(tenantId, employeeId, new DateOnly(2026, 9, 19), new TimeOnly(10, 0), 120, OvertimeDayKind.WeeklyRest, 0, null,
                OvertimeRequestSource.Admin, DateTime.UtcNow, null, null).Value;
            request.AttachApproval(approvalId);
            db.OvertimeRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var bus = ServiceProvider.GetRequiredService<IEventBus>();
        await bus.Publish(new ApprovalCompleted(tenantId, approvalId, ApprovalRequestTypes.Leave, requestId, employeeId));
        await bus.Publish(new ApprovalRejected(tenantId, approvalId, ApprovalRequestTypes.Overtime, requestId, employeeId, 1));

        using var verify = ServiceProvider.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<LeaveDbContext>().OvertimeRequests.SingleAsync(r => r.Id == requestId)).Status.ShouldBe(OvertimeRequestStatus.Rejected);
    }

    // Kart senseik-012 §4 (Puantaj -> Bordro Kontratı): bir Rate25 talep hiç dönüştürülmemiş (nakit ödenecek), bir
    // Rate50 talep serbest zamana çevrilmiş (nakitten düşülür, TimeOffConvertedMinutes'e yazılır).
    [Fact]
    public async Task OvertimePayrollReader_SeparatesRateBucketsFromTimeOffConversions()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid rate50RequestId;

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();

            var rate25 = OvertimeRequest.Submit(
                tenantId, employeeId, new DateOnly(2026, 9, 3), new TimeOnly(17, 0), 120, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, OvertimePayRateKind.Rate25).Value;
            var rate25Approval = Guid.NewGuid();
            rate25.AttachApproval(rate25Approval);
            rate25.Approve(rate25Approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            db.OvertimeRequests.Add(rate25);

            var rate50 = OvertimeRequest.Submit(
                tenantId, employeeId, new DateOnly(2026, 9, 10), new TimeOnly(17, 0), 120, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, OvertimePayRateKind.Rate50).Value;
            var rate50Approval = Guid.NewGuid();
            rate50.AttachApproval(rate50Approval);
            rate50.Approve(rate50Approval, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            db.OvertimeRequests.Add(rate50);
            rate50RequestId = rate50.Id;

            var conversion = OvertimeConversion.ToTimeOff(
                rate50, OvertimeMinutesBasis.Payable, 1.5m, 450, Guid.NewGuid(), null, DateTime.UtcNow, null, new DateOnly(2027, 3, 10)).Value;
            rate50.MarkConverted(conversion.Id).IsSuccess.ShouldBeTrue();
            db.OvertimeConversions.Add(conversion);

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IOvertimePayrollReader>();
        var result = await reader.GetAsync(tenantId, 2026, 9, [employeeId], TestContext.Current.CancellationToken);

        var line = result.ShouldHaveSingleItem();
        line.EmployeeId.ShouldBe(employeeId);
        line.Rate25Minutes.ShouldBe(120); // rate25 talebi hiç dönüştürülmedi -> nakit.
        line.Rate50Minutes.ShouldBe(0); // rate50 talebi dönüştürüldü -> nakit kovasından düşer.
        line.TimeOffConvertedMinutes.ShouldBe(180); // 120dk x 1.5.
    }

    /// <summary>
    /// Kart senseik-030: ConvertOvertimeToPayHandler bir Kind=Pay dönüşümü kaydeder, talebi tek seferlik dönüşüme kilitler
    /// ve outbox'a OvertimeConvertedToPay yazar (Payroll'un dirty-flag mekanizmasının tetikleyicisi). Reader tarafında
    /// hiç dönüştürülmemiş talep ile Kind=Pay dönüştürülmüş talep aynı nakit kovasına düşmeye devam eder - kontrat
    /// değişmedi, IOvertimePayrollReader zaten "Posted TimeOff değilse nakit" kuralını uyguluyor.
    /// </summary>
    [Fact]
    public async Task ConvertOvertimeToPayHandler_PostsConversion_WithoutLedgerRow_AndKeepsCashBucket()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid requestId;

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = OvertimeRequest.Submit(
                tenantId, employeeId, new DateOnly(2026, 9, 3), new TimeOnly(17, 0), 120, OvertimeDayKind.Workday, 0, null,
                OvertimeRequestSource.Manual, DateTime.UtcNow, null, null, OvertimePayRateKind.Rate50).Value;
            var approvalId = Guid.NewGuid();
            request.AttachApproval(approvalId);
            request.Approve(approvalId, DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            db.OvertimeRequests.Add(request);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.Id;
        }

        _dataScope.CanAccessEmployeeAsync(LeavePermissions.OvertimesConvert.Key, employeeId, Arg.Any<CancellationToken>()).Returns(true);

        var converted = await RunAsync<ConvertOvertimeToPayHandler, OvertimeConversionDto>(
            h => h.Handle(new ConvertOvertimeToPayCommand(employeeId, requestId, OvertimeMinutesBasis.Payable, "nakde çevir"), CancellationToken.None),
            _currentUser, _dataScope);
        converted.IsSuccess.ShouldBeTrue(converted.IsFailure ? converted.Error.Code : null);
        converted.Value.Kind.ShouldBe(OvertimeConversionKind.Pay);
        converted.Value.LedgerEntryId.ShouldBeNull();
        converted.Value.Rate.ShouldBe(1.5m); // request.PayRateMultiplier snapshot (Rate50).

        using (var verify = ServiceProvider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var request = await db.OvertimeRequests.SingleAsync(r => r.Id == requestId);
            request.ConversionId.ShouldBe(converted.Value.Id);
            (await db.LedgerEntries.AnyAsync(e => e.SourceRefId == converted.Value.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();
            var outboxTypes = await db.OutboxMessages.Where(m => m.TenantId == tenantId).Select(m => m.Type).ToListAsync();
            outboxTypes.ShouldContain(t => t.EndsWith(nameof(Contracts.Events.OvertimeConvertedToPay), StringComparison.Ordinal));
        }

        // Kontrat tarafı: Kind=Pay dönüştürülmüş talep hâlâ Rate50 nakit kovasında (TimeOff'tan farklı olarak).
        using var read = ServiceProvider.CreateScope();
        var reader = read.ServiceProvider.GetRequiredService<IOvertimePayrollReader>();
        var line = (await reader.GetAsync(tenantId, 2026, 9, [employeeId], TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        line.Rate50Minutes.ShouldBe(120);
        line.TimeOffConvertedMinutes.ShouldBe(0);
    }
}
