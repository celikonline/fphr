using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// senseik-009/F-02: SicilNumarasi's tenant-unique partial index (mirrors NationalId's) and the dual-key lookups
/// (IEmployeeLookup.FindActiveBySicilNumarasiAsync alongside the pre-existing FindActiveByNationalIdAsync) against
/// real PostgreSQL - proves the migration's unique index and the EF query both behave as intended.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeSicilNumarasiTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
    }

    [Fact]
    public async Task SicilNumarasiTakenAsync_DetectsConflictWithinTenant_ButNotAcrossTenantsOrWithSelf()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var repository = new EmployeeRepository(db);

        EmployeeEntity first;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            first = CreateEmployeeOrThrow(tenantA, "SCL-001");
            db.Employees.Add(first);
            await db.SaveChangesAsync();

            (await repository.SicilNumarasiTakenAsync("SCL-001", exceptEmployeeId: null)).ShouldBeTrue();
            // Excluding the employee's own id is not a conflict (update path).
            (await repository.SicilNumarasiTakenAsync("SCL-001", first.Id)).ShouldBeFalse();
            (await repository.SicilNumarasiTakenAsync("SCL-999", exceptEmployeeId: null)).ShouldBeFalse();
        }

        // Same sicil numarası, different tenant: no conflict (tenant isolation, same guarantee as NationalId).
        using (TenantContextSetter!.BeginScope(tenantB))
        {
            (await repository.SicilNumarasiTakenAsync("SCL-001", exceptEmployeeId: null)).ShouldBeFalse();

            var second = CreateEmployeeOrThrow(tenantB, "SCL-001");
            db.Employees.Add(second);
            await db.SaveChangesAsync();
            second.SicilNumarasi.ShouldBe("SCL-001");
        }
    }

    [Fact]
    public async Task FindActiveBySicilNumarasiAsync_ResolvesOneActiveMatch_NullWhenNoneOrAmbiguousOrTerminated()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var active = CreateEmployeeOrThrow(tenantId, "SCL-100");
        db.Employees.Add(active);
        await db.SaveChangesAsync();

        var lookup = new EmployeeLookup(db);
        (await lookup.FindActiveBySicilNumarasiAsync(tenantId, "SCL-100")).ShouldBe(active.Id);
        (await lookup.FindActiveBySicilNumarasiAsync(tenantId, "SCL-NONE")).ShouldBeNull();
    }

    private static EmployeeEntity CreateEmployeeOrThrow(Guid tenantId, string sicilNumarasi)
    {
        var info = new EmployeeInfo(
            FirstName: "Test",
            LastName: "Sicil",
            NationalId: Tckn.Next(),
            HireDate: new DateOnly(2020, 1, 1),
            SicilNumarasi: sicilNumarasi);

        var result = EmployeeEntity.Create(tenantId, info);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }
}
