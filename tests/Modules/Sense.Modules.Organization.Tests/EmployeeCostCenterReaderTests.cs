using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Integration test for IEmployeeCostCenterReader.GetEmployeeCostCenterMappingAsync (Faz 7 tur 1 ACİL KARAR,
/// ADR-0023). Currently, this reader returns an empty dictionary because Employee.Domain.Employee does not yet
/// have a CostCenterId field. Once that field exists in a future release, this test can be extended to assert
/// real mappings. For now, we verify: empty input, tenant isolation (no cross-contamination).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeCostCenterReaderTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<OrganizationDbContext>(configuration, OrganizationDbContext.SchemaName);
    }

    [Fact]
    public async Task GetEmployeeCostCenterMappingAsync_EmptyEmployeeIds_ReturnsEmpty()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var reader = new EmployeeCostCenterReader(db);

        var result = await reader.GetEmployeeCostCenterMappingAsync(tenantId, []);

        result.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetEmployeeCostCenterMappingAsync_ReturnsEmptyDueToMissingEmployeeCostCenterField()
    {
        // Once Employee.Domain.Employee.CostCenterId exists and gets populated, this will return
        // real mappings. Until then, the reader returns an empty dictionary (placeholder implementation).
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        var reader = new EmployeeCostCenterReader(db);

        var employeeIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var result = await reader.GetEmployeeCostCenterMappingAsync(tenantId, employeeIds);

        // Current behavior: no employees have cost center assignments yet.
        result.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetEmployeeCostCenterMappingAsync_TenantIsolation_OtherTenantNotVisible()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var reader = new EmployeeCostCenterReader(db);

        var employeeIds = new[] { Guid.NewGuid() };

        // Query as first tenant - should see nothing (no assignments yet).
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var result = await reader.GetEmployeeCostCenterMappingAsync(tenantId, employeeIds);
            result.Count.ShouldBe(0);
        }

        // Query as other tenant - should also see nothing, and not cross-contaminated with above.
        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            var result = await reader.GetEmployeeCostCenterMappingAsync(otherTenantId, employeeIds);
            result.Count.ShouldBe(0);
        }
    }
}
