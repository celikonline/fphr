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

    /// <summary>Z5.4: FindBySicilNumarasiAsync finds the employee in any status and flags a terminated one (import: "not active" vs "not found").</summary>
    [Fact]
    public async Task FindBySicilNumarasiAsync_FindsAnyStatus_FlagsTerminated_TenantScoped()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var active = CreateEmployeeOrThrow(tenantId, "SCL-300");
        var terminated = CreateEmployeeOrThrow(tenantId, "SCL-301");
        terminated.SetStatus(EmployeeStatus.Terminated);
        db.Employees.AddRange(active, terminated);
        await db.SaveChangesAsync();

        var lookup = new EmployeeLookup(db);
        (await lookup.FindBySicilNumarasiAsync(tenantId, "SCL-300")).ShouldBe(new Sense.Modules.Employee.Contracts.EmployeeSicilMatch(active.Id, false));
        (await lookup.FindBySicilNumarasiAsync(tenantId, "SCL-301")).ShouldBe(new Sense.Modules.Employee.Contracts.EmployeeSicilMatch(terminated.Id, true));
        (await lookup.FindActiveBySicilNumarasiAsync(tenantId, "SCL-301")).ShouldBeNull();
        (await lookup.FindBySicilNumarasiAsync(tenantId, "SCL-NONE")).ShouldBeNull();
        (await lookup.FindBySicilNumarasiAsync(Guid.NewGuid(), "SCL-300")).ShouldBeNull();
    }

    /// <summary>senseik-010/F-03: IEmployeeLookup.GetSgkOccupationCodeAsync surfaces Employee.SgkOccupationCode for Payroll's APHB/e-Bildirge preparation.</summary>
    [Fact]
    public async Task GetSgkOccupationCodeAsync_ReturnsStoredCode_NullWhenNotSetOrEmployeeMissing()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var withCode = CreateEmployeeOrThrow(tenantId, "SCL-200", sgkOccupationCode: "3121.03");
        var withoutCode = CreateEmployeeOrThrow(tenantId, "SCL-201");
        db.Employees.AddRange(withCode, withoutCode);
        await db.SaveChangesAsync();

        var lookup = new EmployeeLookup(db);
        (await lookup.GetSgkOccupationCodeAsync(tenantId, withCode.Id)).ShouldBe("3121.03");
        (await lookup.GetSgkOccupationCodeAsync(tenantId, withoutCode.Id)).ShouldBeNull();
        (await lookup.GetSgkOccupationCodeAsync(tenantId, Guid.NewGuid())).ShouldBeNull();
    }

    private static EmployeeEntity CreateEmployeeOrThrow(Guid tenantId, string sicilNumarasi, string? sgkOccupationCode = null)
    {
        var info = new EmployeeInfo(
            FirstName: "Test",
            LastName: "Sicil",
            NationalId: Tckn.Next(),
            HireDate: new DateOnly(2020, 1, 1),
            SicilNumarasi: sicilNumarasi,
            SgkOccupationCode: sgkOccupationCode);

        var result = EmployeeEntity.Create(tenantId, info);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }
}
