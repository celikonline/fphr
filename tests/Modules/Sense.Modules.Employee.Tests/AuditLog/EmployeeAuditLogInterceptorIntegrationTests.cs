using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests.AuditLog;

/// <summary>
/// senseik-049: Employee'nin özlük bilgisi (işe giriş/doğum tarihi, SGK çıkış kodu vb.) hâlâ hiç denetim kaydına
/// düşmüyordu - senseik-018 yalnız Identity'nin (User/Role) interceptor'ını düzeltmişti, Employee/Leave/Workflow'un
/// DbContext'lerine hiç bağlı değildi. Bu test, IdentityAuditLogInterceptorIntegrationTests'in Employee karşılığı:
/// paylaşımlı AuditLogInterceptor'ın EmployeeDbContext'e bağlandığını ve alan bazlı diff'i (Employee.AuditedFields)
/// gerçek PostgreSQL'e karşı doğrular (KVKK m.12).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeAuditLogInterceptorIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<EmployeeDbContext>(
            configuration, EmployeeDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task CreatingAnEmployee_WritesACreateAuditLogEntry_InTheSameTransaction()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@test.local", new HashSet<string>(StringComparer.Ordinal));

        var employee = EmployeeEntity.Create(tenantId, new EmployeeInfo("Ayse", "Yilmaz")).Value;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.ResourceId == employee.Id.ToString());
        entry.TenantId.ShouldBe(tenantId);
        entry.Action.ShouldBe(AuditActions.Create);
        entry.Resource.ShouldBe("Employee");
    }

    /// <summary>KVKK m.12 çekirdek senaryo: doğum tarihi/işe giriş tarihi/SGK çıkış kodu değişince Details alan adı + öncesi/sonrası değeri gösterir - genel "Employee update" değil.</summary>
    [Fact]
    public async Task UpdatingBirthDateAndHireDate_WritesFieldLevelDiff_NotAGenericSummary()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@test.local", new HashSet<string>(StringComparer.Ordinal));

        var employee = EmployeeEntity.Create(tenantId, new EmployeeInfo("Ayse", "Yilmaz", BirthDate: new DateOnly(1990, 1, 1))).Value;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        employee.UpdateInfo(new EmployeeInfo("Ayse", "Yilmaz", HireDate: new DateOnly(2026, 1, 15), BirthDate: new DateOnly(1990, 5, 20)));
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.ResourceId == employee.Id.ToString() && e.Action == AuditActions.Update)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();

        entry.Details.ShouldContain("BirthDate: 1990-01-01 → 1990-05-20");
        entry.Details.ShouldContain("HireDate: — → 2026-01-15");
        entry.Details.ShouldStartWith($"Employee {AuditActions.Update}:");
    }

    [Fact]
    public async Task UpdatingAFieldNotInAuditedFields_FallsBackToTheGenericSummary()
    {
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@test.local", new HashSet<string>(StringComparer.Ordinal));

        var employee = EmployeeEntity.Create(tenantId, new EmployeeInfo("Ayse", "Yilmaz", WorkPhone: "5551112233")).Value;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // Only WorkPhone changes - not in Employee.AuditedFields, so no diff line to build.
        employee.UpdateInfo(new EmployeeInfo("Ayse", "Yilmaz", WorkPhone: "5559998877"));
        await db.SaveChangesAsync();

        var entry = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.ResourceId == employee.Id.ToString() && e.Action == AuditActions.Update)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();

        entry.Details.ShouldBe($"Employee {AuditActions.Update}");
    }
}
