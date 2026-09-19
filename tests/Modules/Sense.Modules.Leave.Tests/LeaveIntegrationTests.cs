using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Real PostgreSQL (Testcontainers): migration + legislation seed, and Employee.Contracts.EmployeeHired delivered through
/// IEventBus the way Sense.Worker's OutboxProcessor delivers it (handler resolved from DI, tenant scope set by the bus).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveIntegrationTests : TestFixture
{
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
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeavePermissions).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly,
            typeof(Attendance.Contracts.IWorkScheduleReadService).Assembly);

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

    [Fact]
    public void WorkerRegistration_ResolvesEveryLeaveIntegrationEventHandler()
    {
        var configuration = ServiceProvider!.GetRequiredService<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddLeaveCoreServices(configuration);
        services.AddLeaveIntegrationEventHandlers();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmployeeHired>>().ShouldHaveSingleItem();
        // Leave requests and overtime requests each handle Workflow decisions for their own request type.
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<Sense.Modules.Workflow.Contracts.Events.ApprovalCompleted>>().Select(h => h.GetType().Name)
            .ShouldBe(["LeaveApprovalCompletedHandler", "OvertimeApprovalCompletedHandler"], ignoreOrder: true);
        scope.ServiceProvider.GetServices<IIntegrationEventHandler<Sense.Modules.Workflow.Contracts.Events.ApprovalRejected>>().Select(h => h.GetType().Name)
            .ShouldBe(["LeaveApprovalRejectedHandler", "OvertimeApprovalRejectedHandler"], ignoreOrder: true);
    }

    [Fact]
    public async Task LegislationReader_ResolvesSeededDateRanges()
    {
        using var scope = ServiceProvider!.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ILegislationParameterReader>();

        (await reader.GetDecimalAsync(LegislationParameterKeys.PaternityDays, new DateOnly(2026, 4, 30))).ShouldBe(5m);
        (await reader.GetDecimalAsync(LegislationParameterKeys.PaternityDays, new DateOnly(2026, 5, 1))).ShouldBe(10m);
        (await reader.GetDecimalAsync(LegislationParameterKeys.OvertimeConsentRevocationNoticeDays, new DateOnly(2026, 9, 15))).ShouldBe(30m);
        (await reader.FindAsync(LegislationParameterKeys.FosterCareDays, new DateOnly(2026, 1, 1))).ShouldBeNull();

        var all = await reader.ListAsync([LegislationParameterKeys.MaternityPostBirthWeeks]);
        all.Count.ShouldBe(2);
        all.ShouldAllBe(v => v.Source.Contains("4857"));
    }

    [Fact]
    public async Task EmployeeHired_CreatesProfileOnSeededDefaultRuleSet_WithCumulativeAccruals_AndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var accrualStart = new DateOnly(2019, 3, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hired = new EmployeeHired(tenantId, employeeId, new DateOnly(2020, 3, 1), accrualStart);
        var bus = ServiceProvider!.GetRequiredService<IEventBus>();

        await bus.Publish(hired);
        await bus.Publish(hired);

        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var profile = await db.Profiles.SingleAsync();
        profile.EmployeeId.ShouldBe(employeeId);
        profile.LeaveAccrualStartDate.ShouldBe(accrualStart);

        var ruleSet = await db.RuleSets.SingleAsync();
        ruleSet.IsDefault.ShouldBeTrue();
        profile.RuleSetId.ShouldBe(ruleSet.Id);
        var annual = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        annual.SeniorityTiers.Count.ShouldBe(2);
        ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Paternity).PerRequestMaxDays.ShouldBe(10m);

        var accruals = await db.LedgerEntries.Where(e => e.LeaveTypeId == annual.Id && e.Type == LeaveLedgerEntryType.Accrual).ToListAsync();
        var anniversaries = Enumerable.Range(1, 30).Select(accrualStart.AddYears).Where(d => d <= today).ToList();
        accruals.Count.ShouldBe(anniversaries.Count);
        accruals.Sum(e => e.Amount).ShouldBe(anniversaries.Count * 14m + (anniversaries.Count >= 6 ? (anniversaries.Count - 5) * 6m : 0m));

        var carryOvers = await db.LedgerEntries.CountAsync(e => e.LeaveTypeId == annual.Id && e.Type == LeaveLedgerEntryType.CarryOver);
        carryOvers.ShouldBe(Math.Max(0, anniversaries.Count - 1));

        var balances = scope.ServiceProvider.GetRequiredService<Application.Balances.LeaveBalanceService>();
        var projected = await balances.ProjectAsync(profile, ruleSet, TestContext.Current.CancellationToken);
        var annualBalance = projected.Single(b => b.LeaveTypeId == annual.Id);
        annualBalance.TotalAccrued.ShouldBe(accruals.Sum(e => e.Amount));
        annualBalance.CurrentBalance.ShouldBe(annualBalance.TotalAccrued);
    }

    [Fact]
    public async Task RuleSet_ClonedAndPersisted_RoundTripsTypesAndTiers()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        Guid cloneId;

        using (var write = ServiceProvider!.CreateScope())
        {
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var seed = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            db.RuleSets.Add(seed);
            await db.SaveChangesAsync();

            var clone = seed.Clone("Kopya").Value;
            db.RuleSets.Add(clone);
            await db.SaveChangesAsync();
            cloneId = clone.Id;
        }

        using var read = ServiceProvider.CreateScope();
        var repo = read.ServiceProvider.GetRequiredService<ILeaveRuleSetRepository>();
        var loaded = (await repo.GetByIdAsync(cloneId)).Value;
        loaded.IsDefault.ShouldBeFalse();
        loaded.Types.Count.ShouldBe(LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).Count());
        loaded.Types.Single(t => t.Code == LeaveTypeCode.Annual).SeniorityTiers.Select(t => t.ExtraDays).ShouldBe([6m, 6m]);
        (await repo.FindDefaultAsync())!.Id.ShouldNotBe(cloneId);
    }

    /// <summary>
    /// Absence (11.3.5 kapsamı dışı): `20260916143056_AddAbsence` (main) migrates leave.absence_records - this round-trip
    /// used to be Skip-gated on that migration (ADR-0005); domain rules are also covered DB-free by AbsenceRecordTests.
    /// </summary>
    [Fact]
    public async Task AbsenceRecord_RecordedThenReversed_RoundTripsAndReversalIsNotReReversible()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordedByUserId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        Guid originalId;
        using (var write = ServiceProvider!.CreateScope())
        {
            var repo = write.ServiceProvider.GetRequiredService<IAbsenceRecordRepository>();
            var db = write.ServiceProvider.GetRequiredService<LeaveDbContext>();
            var original = AbsenceRecord.Record(
                tenantId, employeeId, new DateOnly(2026, 9, 15), AbsenceKind.Unexcused, null, "Haber vermeden gelmedi", recordedByUserId);
            original.IsSuccess.ShouldBeTrue();
            repo.Add(original.Value);
            await db.SaveChangesAsync();
            originalId = original.Value.Id;
        }

        using var read = ServiceProvider.CreateScope();
        var readRepo = read.ServiceProvider.GetRequiredService<IAbsenceRecordRepository>();
        var readDb = read.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var original2 = await readRepo.FindByIdAsync(originalId);
        (await readRepo.ExistsReversalAsync(originalId)).ShouldBeFalse();

        var reversal = AbsenceRecord.Reverse(original2!, "Yanlış girildi", recordedByUserId, alreadyReversed: false);
        reversal.IsSuccess.ShouldBeTrue();
        readRepo.Add(reversal.Value);
        await readDb.SaveChangesAsync();

        (await readRepo.ExistsReversalAsync(originalId)).ShouldBeTrue();
        var reReverse = AbsenceRecord.Reverse(reversal.Value, "Tekrar", recordedByUserId, alreadyReversed: false);
        reReverse.IsFailure.ShouldBeTrue();
    }
}
