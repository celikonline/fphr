using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Infrastructure.Lookups;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>
/// PAY-INCENTIVE-3 on real PostgreSQL: <see cref="EmployeeIncentiveLookup.HasActiveIncentiveAsync"/> means "the profile has an incentive law
/// code OR an assignment covering the month of asOf points at a usable definition" (the signature is unchanged).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class IncentiveLookupIntegrationTests : TestFixture
{
    private static readonly DateOnly March = new(2026, 3, 15);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<PayrollDbContext>(configuration, PayrollDbContext.SchemaName);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<PayrollDbContext>().Database.MigrateAsync();

    private static EmployeePayrollProfile Profile(Guid tenantId, Guid employeeId, string? incentiveLawCode) => EmployeePayrollProfile.Create(
        tenantId, employeeId,
        new EmployeePayrollProfileData(
            Guid.NewGuid(), null, new DateOnly(2026, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, incentiveLawCode,
            MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null, false, null, null)).Value;

    private async Task<bool> LookupAsync(Guid tenantId, Guid employeeId)
    {
        using var scope = ServiceProvider!.CreateScope();
        var lookup = new EmployeeIncentiveLookup(scope.ServiceProvider.GetRequiredService<PayrollDbContext>());
        return await lookup.HasActiveIncentiveAsync(tenantId, employeeId, March);
    }

    private async Task AssignAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly? to, bool deactivate = false)
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        var definition = IncentiveDefinition.Create(
            tenantId, "SGK_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            new IncentiveDefinitionData("Test", null, "06111", IncentiveKind.DeclarationOnly, IncentiveParameters.Empty, null, new DateOnly(2026, 1, 1), null)).Value;
        db.IncentiveDefinitions.Add(definition);
        db.IncentiveAssignments.Add(EmployeeIncentiveAssignment.Create(
            tenantId, employeeId, definition, from, to, null, new HashSet<DateOnly>(), [], _ => definition.Category).Value);
        await db.SaveChangesAsync();
        if (deactivate)
        {
            definition.Deactivate();
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task WithoutAProfileCodeOrAnAssignment_ThereIsNoIncentive()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        (await LookupAsync(tenantId, Guid.NewGuid())).ShouldBeFalse();
    }

    [Fact]
    public async Task AProfileIncentiveCode_StillCounts()
    {
        var tenantId = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        using (var scope = ServiceProvider!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
            db.EmployeePayrollProfiles.Add(Profile(tenantId, employee, "05510"));
            await db.SaveChangesAsync();
        }

        (await LookupAsync(tenantId, employee)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnAssignmentCoveringTheMonth_CountsWithoutAProfileCode()
    {
        var tenantId = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await AssignAsync(tenantId, employee, new DateOnly(2026, 3, 1), null);

        (await LookupAsync(tenantId, employee)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnAssignmentThatEndedBeforeTheMonthOrStartsAfterIt_DoesNotCount()
    {
        var tenantId = Guid.NewGuid();
        var ended = Guid.NewGuid();
        var future = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await AssignAsync(tenantId, ended, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        await AssignAsync(tenantId, future, new DateOnly(2026, 4, 1), null);

        (await LookupAsync(tenantId, ended)).ShouldBeFalse();
        (await LookupAsync(tenantId, future)).ShouldBeFalse();
    }

    [Fact]
    public async Task AnAssignmentOfAnInactiveDefinition_DoesNotCount()
    {
        var tenantId = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        await AssignAsync(tenantId, employee, new DateOnly(2026, 3, 1), null, deactivate: true);

        (await LookupAsync(tenantId, employee)).ShouldBeFalse();
    }

    [Fact]
    public async Task AnotherTenantsAssignment_DoesNotCount()
    {
        var otherTenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        using (TenantContextSetter!.BeginScope(otherTenant))
        {
            await AssignAsync(otherTenant, employee, new DateOnly(2026, 3, 1), null);
        }

        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);

        (await LookupAsync(tenantId, employee)).ShouldBeFalse();
    }
}
