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
/// senseik-007 (A-01 regresyon): IEmployeeBirthDateLookup is the only port through which Leave may read BirthDate
/// (sensitive/KVKK - never carried on EmployeeHired). Covers the single lookup, the batched lookup used by
/// LeaveBalanceRecalculationJob's nightly pass (N+1 avoidance), and tenant isolation.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeBirthDateLookupTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
    }

    [Fact]
    public async Task GetBirthDateAsync_ReturnsTheEmployeesBirthDate_AndNull_WhenNotRecordedOrNotFound()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var withBirthDate = CreateEmployeeOrThrow(tenantId, new DateOnly(1970, 6, 15));
        var withoutBirthDate = CreateEmployeeOrThrow(tenantId, birthDate: null);
        db.Employees.AddRange(withBirthDate, withoutBirthDate);
        await db.SaveChangesAsync();

        var lookup = new EmployeeBirthDateLookup(db);

        (await lookup.GetBirthDateAsync(tenantId, withBirthDate.Id)).ShouldBe(new DateOnly(1970, 6, 15));
        (await lookup.GetBirthDateAsync(tenantId, withoutBirthDate.Id)).ShouldBeNull();
        (await lookup.GetBirthDateAsync(tenantId, Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task GetBirthDatesAsync_ReturnsOneRoundTripBatch_OmittingEmployeesWithNoBirthDate()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var first = CreateEmployeeOrThrow(tenantId, new DateOnly(1975, 1, 1));
        var second = CreateEmployeeOrThrow(tenantId, new DateOnly(1990, 12, 31));
        var noBirthDate = CreateEmployeeOrThrow(tenantId, birthDate: null);
        db.Employees.AddRange(first, second, noBirthDate);
        await db.SaveChangesAsync();

        var lookup = new EmployeeBirthDateLookup(db);
        var result = await lookup.GetBirthDatesAsync(tenantId, [first.Id, second.Id, noBirthDate.Id, Guid.NewGuid()]);

        result.Count.ShouldBe(2);
        result[first.Id].ShouldBe(new DateOnly(1975, 1, 1));
        result[second.Id].ShouldBe(new DateOnly(1990, 12, 31));
        result.ContainsKey(noBirthDate.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task GetBirthDatesAsync_EmptyInput_ReturnsEmptyWithoutQuerying()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var lookup = new EmployeeBirthDateLookup(db);
        var result = await lookup.GetBirthDatesAsync(Guid.NewGuid(), []);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetBirthDateAsync_DoesNotLeakAcrossTenants()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        EmployeeEntity employeeInTenantA;
        using (TenantContextSetter!.BeginScope(tenantA))
        {
            employeeInTenantA = CreateEmployeeOrThrow(tenantA, new DateOnly(1980, 3, 3));
            db.Employees.Add(employeeInTenantA);
            await db.SaveChangesAsync();
        }

        var lookup = new EmployeeBirthDateLookup(db);
        using (TenantContextSetter!.BeginScope(tenantB))
        {
            // Same employee id, wrong tenant in scope + passed explicitly: must not resolve.
            (await lookup.GetBirthDateAsync(tenantB, employeeInTenantA.Id)).ShouldBeNull();
        }

        using (TenantContextSetter!.BeginScope(tenantA))
        {
            (await lookup.GetBirthDateAsync(tenantA, employeeInTenantA.Id)).ShouldBe(new DateOnly(1980, 3, 3));
        }
    }

    private static EmployeeEntity CreateEmployeeOrThrow(Guid tenantId, DateOnly? birthDate)
    {
        var info = new EmployeeInfo(
            FirstName: "Test",
            LastName: "Yaş",
            NationalId: Tckn.Next(),
            HireDate: new DateOnly(2020, 1, 1),
            BirthDate: birthDate);

        var result = EmployeeEntity.Create(tenantId, info);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }
}
