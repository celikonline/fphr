using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
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
public sealed class LeaveIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddLeaveServices(configuration);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeavePermissions).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();

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
}
