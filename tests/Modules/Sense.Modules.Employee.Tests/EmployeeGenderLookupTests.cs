using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>Denetim raporu A6: IEmployeeLookup.GetGenderAsync feeds Leave's gender-restricted types (Babalık/Doğum/Süt) on real PostgreSQL.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeGenderLookupTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
    }

    [Fact]
    public async Task GetGenderAsync_MapsTheStoredGender_NullForAnotherTenantOrMissingEmployee()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        var woman = Create(tenantId, Gender.Female);
        var man = Create(tenantId, Gender.Male);
        var unrecorded = Create(tenantId, Gender.Unspecified);
        db.Employees.AddRange(woman, man, unrecorded);
        await db.SaveChangesAsync();

        var lookup = new EmployeeLookup(db);
        (await lookup.GetGenderAsync(tenantId, woman.Id)).ShouldBe(EmployeeGender.Female);
        (await lookup.GetGenderAsync(tenantId, man.Id)).ShouldBe(EmployeeGender.Male);
        (await lookup.GetGenderAsync(tenantId, unrecorded.Id)).ShouldBe(EmployeeGender.Unspecified);
        (await lookup.GetGenderAsync(tenantId, Guid.NewGuid())).ShouldBeNull();
        (await lookup.GetGenderAsync(Guid.NewGuid(), woman.Id)).ShouldBeNull();
    }

    private static EmployeeEntity Create(Guid tenantId, Gender gender)
    {
        var created = EmployeeEntity.Create(tenantId, new EmployeeInfo("Test", "Cinsiyet", Tckn.Next(), HireDate: new DateOnly(2020, 1, 1), Gender: gender));
        created.IsSuccess.ShouldBeTrue();
        return created.Value;
    }
}
