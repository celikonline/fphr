using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
// Alias to avoid clashing with the "Sense.Modules.Employee" namespace segment - see EmployeeDbContext.cs.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// EmployeeAnalyticsQueries against PostgreSQL: active headcount, career-path change counts (CAREER_PATH_RATIO
/// prerequisite, roadmap #36/#99) and gender headcount, all tenant-scoped.
/// </summary>
[Collection("PostgreSQL")]
public sealed class EmployeeAnalyticsQueriesTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new EmployeeModule().AddModule(services, configuration);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task GetActiveHeadcountAsync_CountsOnlyActiveEmployees_ForTheGivenTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var context = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            context.Employees.Add(NewActiveEmployee(tenantId, "Active", "One"));
            context.Employees.Add(NewDraftEmployee(tenantId, "Draft", "Two"));
            await context.SaveChangesAsync();
        }

        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            context.Employees.Add(NewActiveEmployee(otherTenantId, "Other", "Tenant"));
            await context.SaveChangesAsync();
        }

        // The tenant query filter reads the ambient ITenantContext, so the read needs its own scope too.
        var queries = new EmployeeAnalyticsQueries(context);
        int headcount;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            headcount = await queries.GetActiveHeadcountAsync(tenantId, CancellationToken.None);
        }

        headcount.ShouldBe(1);
    }

    [Fact]
    public async Task GetCareerPathChangeCountsAsync_GroupsByChangeType_WithinPeriod_ExcludingHire()
    {
        var tenantId = Guid.NewGuid();
        var context = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var careerContext = new CareerContext(new DateOnly(2026, 6, 1), new Dictionary<Guid, int>(), new Dictionary<Guid, Guid?>());

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var employee = NewActiveEmployee(tenantId, "Career", "Path", hireDate: new DateOnly(2020, 1, 1));
            employee.AddCareerRecord(new CareerRecordData(new DateOnly(2026, 3, 1), CareerChangeType.Promotion), careerContext).IsSuccess.ShouldBeTrue();
            employee.AddCareerRecord(new CareerRecordData(new DateOnly(2026, 6, 1), CareerChangeType.LateralMove), careerContext).IsSuccess.ShouldBeTrue();
            // Outside the queried period: must not be counted.
            employee.AddCareerRecord(new CareerRecordData(new DateOnly(2027, 1, 1), CareerChangeType.TitleChange), careerContext).IsSuccess.ShouldBeTrue();
            context.Employees.Add(employee);
            await context.SaveChangesAsync();
        }

        var queries = new EmployeeAnalyticsQueries(context);
        CareerPathChangeCounts counts;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            counts = await queries.GetCareerPathChangeCountsAsync(tenantId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);
        }

        counts.Promotion.ShouldBe(1);
        counts.LateralMove.ShouldBe(1);
        counts.Demotion.ShouldBe(0);
        counts.TitleChange.ShouldBe(0);
        counts.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GetGenderHeadcountAsync_GroupsActiveEmployeesByGender()
    {
        var tenantId = Guid.NewGuid();
        var context = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            context.Employees.Add(NewActiveEmployee(tenantId, "A", "Female", gender: Gender.Female));
            context.Employees.Add(NewActiveEmployee(tenantId, "B", "Female", gender: Gender.Female));
            context.Employees.Add(NewActiveEmployee(tenantId, "C", "Male", gender: Gender.Male));
            await context.SaveChangesAsync();
        }

        var queries = new EmployeeAnalyticsQueries(context);
        IReadOnlyDictionary<string, int> counts;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            counts = await queries.GetGenderHeadcountAsync(tenantId, CancellationToken.None);
        }

        counts[nameof(Gender.Female)].ShouldBe(2);
        counts[nameof(Gender.Male)].ShouldBe(1);
    }

    private static EmployeeEntity NewActiveEmployee(Guid tenantId, string firstName, string lastName, DateOnly? hireDate = null, Gender gender = Gender.Unspecified) =>
        EmployeeEntity.Create(tenantId, new EmployeeInfo(firstName, lastName, HireDate: hireDate ?? new DateOnly(2024, 1, 1), Gender: gender)).Value;

    private static EmployeeEntity NewDraftEmployee(Guid tenantId, string firstName, string lastName) =>
        EmployeeEntity.Create(tenantId, new EmployeeInfo(firstName, lastName)).Value;
}
