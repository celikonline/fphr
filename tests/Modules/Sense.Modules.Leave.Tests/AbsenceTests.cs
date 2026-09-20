using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Absences;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// RecordAbsenceHandler's leave conflict gate (G-5-09), ListEmployeeAbsencesHandler's note visibility (G-5-11) and
/// ILeaveUsageReader (S-18) on real PostgreSQL. Absence domain shape rules are covered DB-free by AbsenceRecordTests
/// (LeaveDomainTests.cs); this file only covers the handler-level rules that need an employee/rule set/ledger to exist.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AbsenceIntegrationTests : TestFixture
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IUserProfileLookup _userProfiles = Substitute.For<IUserProfileLookup>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        // AddLeaveServices' IWorkScheduleReader adapts Attendance.Contracts.IWorkScheduleReadService (roadmap #23).
        services.AddModuleDbContext<AttendanceDbContext>(configuration, AttendanceDbContext.SchemaName);
        services.AddAttendanceCoreServices(configuration);
        // roadmap #23 (A1): EmployeeHired -> default schedule assignment must run here too, otherwise the
        // real IWorkScheduleReadService reports every day closed and all submits fail with zero_days.
        services.AddAttendanceIntegrationEventHandlers();
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName, typeof(LeaveErrors).Assembly, typeof(LeavePermissions).Assembly, typeof(LeaveDbContext).Assembly, typeof(ILeaveRuleSetRepository).Assembly, typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);

        // RecordAbsenceHandler now only accepts working days (denetim A10). No employee is hired in these tests, so the real
        // schedule reader would report every day closed; a Monday-Friday reader and an empty holiday calendar stand in for it.
        var schedules = Substitute.For<IWorkScheduleReader>();
        schedules.GetWorkingDayPredicateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((Func<DateOnly, bool>)(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)));
        schedules.GetIncludeSaturdayWhenFullWeekPredicateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((Func<DateOnly, bool>)(_ => false));
        services.AddSingleton(schedules);
        var holidays = Substitute.For<IHolidayCalendarLookup>();
        holidays.ResolveCalendarAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((HolidayCalendarRef?)null);
        services.AddSingleton(holidays);

        _currentUser.UserId.Returns(Guid.NewGuid());
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _userProfiles.GetAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserProfileInfo>());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
    }

    private async Task<Guid> SeedApprovedLeaveAsync(
        Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var request = LeaveRequest.Submit(
            tenantId, employeeId, Guid.NewGuid(), LeaveTypeCode.Annual, date, date, halfDayPeriod is not null ? 0.5m : 1m, null,
            LeaveRequestSource.SelfService, DateTime.UtcNow, null, null,
            startTime: startTime, endTime: endTime, halfDayPeriod: halfDayPeriod).Value;
        var approvalId = Guid.NewGuid();
        request.AttachApproval(approvalId);
        request.Approve(approvalId, DateTime.UtcNow);
        db.Requests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<Result<Guid>> RecordAsync(RecordAbsenceCommand command)
    {
        using var scope = ServiceProvider!.CreateScope();
        var result = await ActivatorUtilities.CreateInstance<RecordAbsenceHandler>(scope.ServiceProvider, _currentUser, _dataScope).Handle(command, CancellationToken.None);
        if (result.IsSuccess)
        {
            await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().SaveChangesAsync();
        }

        return result;
    }

    /// <summary>G-5-09: an Unexcused absence on a date an Approved full-day leave already covers is rejected.</summary>
    [Fact]
    public async Task RecordAbsence_Unexcused_OnApprovedFullDayLeaveDate_ConflictsWithLeave()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 12);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedApprovedLeaveAsync(tenantId, employeeId, date);

        var result = await RecordAsync(new RecordAbsenceCommand(employeeId, date, AbsenceKind.Unexcused, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveErrors.AbsenceConflictsWithLeave);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    /// <summary>G-5-09: the Late kind is exempt from the conflict check even on an Approved full-day leave date.</summary>
    [Fact]
    public async Task RecordAbsence_Late_OnApprovedFullDayLeaveDate_IsNotBlocked()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 12);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedApprovedLeaveAsync(tenantId, employeeId, date);

        var result = await RecordAsync(new RecordAbsenceCommand(employeeId, date, AbsenceKind.Late, 1m, null));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
    }

    /// <summary>G-5-09: a HalfDay leave does not block an Excused absence on the same date (only a full-day approved leave does).</summary>
    [Fact]
    public async Task RecordAbsence_Excused_OnApprovedHalfDayLeaveDate_IsNotBlocked()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 12);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await SeedApprovedLeaveAsync(tenantId, employeeId, date, halfDayPeriod: HalfDayPeriod.Morning);

        var result = await RecordAsync(new RecordAbsenceCommand(employeeId, date, AbsenceKind.Excused, null, null));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
    }

    /// <summary>G-5-11: a caller holding leave.absences.manage sees the note.</summary>
    [Fact]
    public async Task ListEmployeeAbsences_CallerWithManagePermission_SeesNote()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 12);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        (await RecordAsync(new RecordAbsenceCommand(employeeId, date, AbsenceKind.Unexcused, null, "Haber vermeden gelmedi"))).IsSuccess.ShouldBeTrue();
        _permissions.HasAsync(Arg.Any<Guid>(), LeavePermissions.AbsencesManage.Key, Arg.Any<CancellationToken>()).Returns(true);

        using var scope = ServiceProvider!.CreateScope();
        var result = await ActivatorUtilities.CreateInstance<ListEmployeeAbsencesHandler>(scope.ServiceProvider, _currentUser, _permissions, _dataScope, _userProfiles)
            .Handle(new ListEmployeeAbsencesQuery(employeeId, date, date), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().Note.ShouldBe("Haber vermeden gelmedi");
    }

    /// <summary>G-5-11: a caller holding only leave.absences.view (not manage) never sees the note - it is dropped from the DTO, not empty.</summary>
    [Fact]
    public async Task ListEmployeeAbsences_CallerWithoutManagePermission_NoteIsNull()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 12);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        (await RecordAsync(new RecordAbsenceCommand(employeeId, date, AbsenceKind.Unexcused, null, "Haber vermeden gelmedi"))).IsSuccess.ShouldBeTrue();
        _permissions.HasAsync(Arg.Any<Guid>(), LeavePermissions.AbsencesManage.Key, Arg.Any<CancellationToken>()).Returns(false);

        using var scope = ServiceProvider!.CreateScope();
        var result = await ActivatorUtilities.CreateInstance<ListEmployeeAbsencesHandler>(scope.ServiceProvider, _currentUser, _permissions, _dataScope, _userProfiles)
            .Handle(new ListEmployeeAbsencesQuery(employeeId, date, date), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().Note.ShouldBeNull();
    }

    /// <summary>
    /// S-18: TotalEntitledDays sums only the Annual type's Accrual+CarryOver dated in the year; UsedDays is the Annual
    /// type's netted Usage/Reversal that year; ByLeaveType covers every type (Marriage here) with nonzero usage. A row
    /// dated the year before is excluded from every bucket.
    /// </summary>
    [Fact]
    public async Task LeaveUsageReader_GetAnnualUsage_AggregatesEntitlementAndUsageInsideTheYear()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid annualTypeId;
        Guid marriageTypeId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            annualTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
            marriageTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Marriage).Id;

            db.LedgerEntries.AddRange(
                LeaveLedgerEntry.Accrual(tenantId, employeeId, annualTypeId, 14m, new DateOnly(2026, 1, 1)),
                LeaveLedgerEntry.CarryOver(tenantId, employeeId, annualTypeId, 3m, new DateOnly(2026, 1, 1)),
                LeaveLedgerEntry.Usage(tenantId, employeeId, annualTypeId, 5m, new DateOnly(2026, 6, 1), LeaveLedgerSource.Request, Guid.NewGuid()),
                LeaveLedgerEntry.Usage(tenantId, employeeId, marriageTypeId, 3m, new DateOnly(2026, 7, 1), LeaveLedgerSource.Request, Guid.NewGuid()),
                // Excluded: dated in the previous year.
                LeaveLedgerEntry.Accrual(tenantId, employeeId, annualTypeId, 14m, new DateOnly(2025, 1, 1)),
                LeaveLedgerEntry.Usage(tenantId, employeeId, annualTypeId, 2m, new DateOnly(2025, 12, 1), LeaveLedgerSource.Request, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ILeaveUsageReader>();
        var summary = await reader.GetAnnualUsageAsync(tenantId, 2026, null, CancellationToken.None);

        summary.TotalEntitledDays.ShouldBe(17m);
        summary.UsedDays.ShouldBe(5m);
        summary.ByLeaveType.Count.ShouldBe(2);
        summary.ByLeaveType.Single(u => u.LeaveTypeId == annualTypeId).UsedDays.ShouldBe(5m);
        var marriage = summary.ByLeaveType.Single(u => u.LeaveTypeId == marriageTypeId);
        marriage.UsedDays.ShouldBe(3m);
        marriage.LeaveTypeName.ShouldBe("leave.type.marriage");

        var scoped = await reader.GetAnnualUsageAsync(tenantId, 2026, [Guid.NewGuid()], CancellationToken.None);
        scoped.TotalEntitledDays.ShouldBe(0m);
        scoped.UsedDays.ShouldBe(0m);
        scoped.ByLeaveType.ShouldBeEmpty();
    }

    /// <summary>
    /// S-18 kenar vakası (a): a CarryOver row is dated inside the year it is carried INTO (LeaveBalanceEngine.Regenerate
    /// dates CarryOver at the new period's start), so year Y's TotalEntitledDays is that year's own Accrual+CarryOver -
    /// the previous year's CarryOver row (dated the year before) must not bleed into year Y and vice versa.
    /// </summary>
    [Fact]
    public async Task LeaveUsageReader_GetAnnualUsage_CarryOverFromPriorYear_CountsOnlyInTheYearItLandsIn()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid annualTypeId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            annualTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;

            db.LedgerEntries.AddRange(
                // 2025: 14 accrued + 2 carried over from 2024 -> 16.
                LeaveLedgerEntry.Accrual(tenantId, employeeId, annualTypeId, 14m, new DateOnly(2025, 1, 1)),
                LeaveLedgerEntry.CarryOver(tenantId, employeeId, annualTypeId, 2m, new DateOnly(2025, 1, 1)),
                // 2026: 14 accrued + 3 carried over from 2025 -> 17.
                LeaveLedgerEntry.Accrual(tenantId, employeeId, annualTypeId, 14m, new DateOnly(2026, 1, 1)),
                LeaveLedgerEntry.CarryOver(tenantId, employeeId, annualTypeId, 3m, new DateOnly(2026, 1, 1)));
            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ILeaveUsageReader>();

        (await reader.GetAnnualUsageAsync(tenantId, 2025, null, CancellationToken.None)).TotalEntitledDays.ShouldBe(16m);
        (await reader.GetAnnualUsageAsync(tenantId, 2026, null, CancellationToken.None)).TotalEntitledDays.ShouldBe(17m);
    }

    /// <summary>
    /// S-18 kenar vakası (b): an approved Annual leave that is then cancelled posts a Reversal row dated the same as the
    /// Usage row it offsets (LeaveLedgerEntry.Reversal), so both land in the same year and net to zero - UsedDays is
    /// unaffected and the zeroed type is dropped from ByLeaveType (not listed with UsedDays = 0).
    /// </summary>
    [Fact]
    public async Task LeaveUsageReader_GetAnnualUsage_ReversedLeave_IsNettedOutOfUsedDaysAndByLeaveType()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid annualTypeId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(ruleSet);
            annualTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;

            var usage = LeaveLedgerEntry.Usage(tenantId, employeeId, annualTypeId, 5m, new DateOnly(2026, 6, 1), LeaveLedgerSource.Request, Guid.NewGuid());
            db.LedgerEntries.Add(usage);
            db.LedgerEntries.Add(LeaveLedgerEntry.Reversal(usage, "Talep iptal edildi"));
            await db.SaveChangesAsync();
        }

        using var scope = ServiceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ILeaveUsageReader>();
        var summary = await reader.GetAnnualUsageAsync(tenantId, 2026, null, CancellationToken.None);

        summary.UsedDays.ShouldBe(0m);
        summary.ByLeaveType.ShouldNotContain(u => u.LeaveTypeId == annualTypeId);
    }
}
