using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Jobs;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// senseik-007 (A-01 regresyon): proves the retroactive backfill the nightly job now performs - a profile whose
/// BirthDate is still null (created before IEmployeeBirthDateLookup existed) gets it filled from Employee via a
/// single batched call, and the Annual ledger is regenerated so the ≤18/≥50 statutory age exception (4857 md.53/IV)
/// takes effect immediately, without a new EmployeeHired event. See docs/handoffs/age-50-exception.md §5: this is
/// exactly the retroactive-balance-increase behavior that must not ship silently.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LeaveBalanceRecalculationJobTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddLeaveCoreServices(configuration);
        services.AddLeaveBalanceRecalculationJob();

        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
        services.AddScoped<IEmployeeBirthDateLookup, EmployeeBirthDateLookup>();
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<LeaveDbContext>().Database.MigrateAsync();
        await ServiceProvider.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task RunAsync_BackfillsNullBirthDate_AndRaisesTheAnnualAccrualToTheAgeFloor()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // 2 completed years -> Tier1 (14) alone, well below the tier2 threshold (>5y): isolates the age exception as
        // the only reason the balance should rise.
        var hireDate = today.AddYears(-2);
        var birthDate = today.AddYears(-56); // well over OldAgeMin (50)

        Guid annualTypeId;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var employeeDb = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
            var employee = EmployeeEntity.Create(tenantId, new EmployeeInfo(FirstName: "Test", LastName: "Yaş", HireDate: hireDate, BirthDate: birthDate), id: employeeId);
            employee.IsSuccess.ShouldBeTrue();
            employeeDb.Employees.Add(employee.Value);
            await employeeDb.SaveChangesAsync();

            var leaveDb = ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            annualTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
            leaveDb.RuleSets.Add(ruleSet);
            await leaveDb.SaveChangesAsync();

            // BirthDate stays null here - simulates a profile created (by the old, un-wired EmployeeHiredHandler)
            // before senseik-007. This is the exact state the nightly job's backfill must repair.
            var profile = EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, hireDate, hireDate);
            leaveDb.Profiles.Add(profile);
            await leaveDb.SaveChangesAsync();
        }

        var job = ServiceProvider!.GetRequiredService<LeaveBalanceRecalculationJob>();
        var processed = await job.RunAsync(CancellationToken.None);

        processed.ShouldBeGreaterThanOrEqualTo(1);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var leaveDb = ServiceProvider.GetRequiredService<LeaveDbContext>();

            var updatedProfile = await leaveDb.Profiles.AsNoTracking().SingleAsync(p => p.EmployeeId == employeeId);
            updatedProfile.BirthDate.ShouldBe(birthDate);

            var accruals = await leaveDb.LedgerEntries.AsNoTracking()
                .Where(e => e.EmployeeId == employeeId && e.LeaveTypeId == annualTypeId && e.Type == LeaveLedgerEntryType.Accrual)
                .ToListAsync();
            accruals.ShouldContain(e => e.Amount == 20m);
            accruals.ShouldNotContain(e => e.Amount == 14m);
        }
    }

    [Fact]
    public async Task RunAsync_EmployeeWithNoRecordedBirthDate_LeavesProfileBirthDateNull_AndKeepsSeniorityTierOnly()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hireDate = today.AddYears(-2);

        Guid annualTypeId;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var employeeDb = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
            var employee = EmployeeEntity.Create(tenantId, new EmployeeInfo(FirstName: "Test", LastName: "NoBirthDate", HireDate: hireDate, BirthDate: null), id: employeeId);
            employee.IsSuccess.ShouldBeTrue();
            employeeDb.Employees.Add(employee.Value);
            await employeeDb.SaveChangesAsync();

            var leaveDb = ServiceProvider.GetRequiredService<LeaveDbContext>();
            var ruleSet = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(tenantId, StatutoryLeaveDurations.Turkey2026);
            annualTypeId = ruleSet.Types.Single(t => t.Code == LeaveTypeCode.Annual).Id;
            leaveDb.RuleSets.Add(ruleSet);
            await leaveDb.SaveChangesAsync();

            var profile = EmployeeLeaveProfile.Create(tenantId, employeeId, ruleSet.Id, hireDate, hireDate);
            leaveDb.Profiles.Add(profile);
            await leaveDb.SaveChangesAsync();
        }

        var job = ServiceProvider!.GetRequiredService<LeaveBalanceRecalculationJob>();
        await job.RunAsync(CancellationToken.None);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var leaveDb = ServiceProvider.GetRequiredService<LeaveDbContext>();

            var updatedProfile = await leaveDb.Profiles.AsNoTracking().SingleAsync(p => p.EmployeeId == employeeId);
            updatedProfile.BirthDate.ShouldBeNull();

            var accruals = await leaveDb.LedgerEntries.AsNoTracking()
                .Where(e => e.EmployeeId == employeeId && e.LeaveTypeId == annualTypeId && e.Type == LeaveLedgerEntryType.Accrual)
                .ToListAsync();
            accruals.ShouldContain(e => e.Amount == 14m);
            accruals.ShouldNotContain(e => e.Amount == 20m);
        }
    }
}
