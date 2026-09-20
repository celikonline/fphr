using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
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
[Trait("Category", "Integration")]
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
        var queries = NewQueries(context);
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

        var queries = NewQueries(context);
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

        var queries = NewQueries(context);
        IReadOnlyDictionary<string, int> counts;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            counts = await queries.GetGenderHeadcountAsync(tenantId, CancellationToken.None);
        }

        counts[nameof(Gender.Female)].ShouldBe(2);
        counts[nameof(Gender.Male)].ShouldBe(1);
    }

    [Fact]
    public async Task GetTerminationCountsAsync_CountsTerminationsInPeriod_AndSplitsEarlyAttrition()
    {
        var tenantId = Guid.NewGuid();
        var context = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            // Terminated 3 months after hire: within the "first 6 months" slice.
            var earlyLeaver = NewActiveEmployee(tenantId, "Early", "Leaver", hireDate: new DateOnly(2026, 1, 1));
            earlyLeaver.Terminate(new DateOnly(2026, 4, 1), Guid.NewGuid(), TerminationType.Resignation, sgkExitCode: null).IsSuccess.ShouldBeTrue();
            context.Employees.Add(earlyLeaver);

            // Terminated after 2 years: not early attrition.
            var tenuredLeaver = NewActiveEmployee(tenantId, "Tenured", "Leaver", hireDate: new DateOnly(2024, 1, 1));
            tenuredLeaver.Terminate(new DateOnly(2026, 6, 1), Guid.NewGuid(), TerminationType.Resignation, sgkExitCode: null).IsSuccess.ShouldBeTrue();
            context.Employees.Add(tenuredLeaver);

            // Terminated outside the queried period: must not be counted.
            var outsidePeriod = NewActiveEmployee(tenantId, "Outside", "Period", hireDate: new DateOnly(2020, 1, 1));
            outsidePeriod.Terminate(new DateOnly(2027, 2, 1), Guid.NewGuid(), TerminationType.Resignation, sgkExitCode: null).IsSuccess.ShouldBeTrue();
            context.Employees.Add(outsidePeriod);

            context.Employees.Add(NewActiveEmployee(tenantId, "Still", "Active"));

            await context.SaveChangesAsync();
        }

        var queries = NewQueries(context);
        TerminationCounts counts;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            counts = await queries.GetTerminationCountsAsync(tenantId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);
        }

        counts.Total.ShouldBe(2);
        counts.WithinFirstSixMonths.ShouldBe(1);
    }

    [Fact]
    public async Task GetGenderByOrgUnitAsync_GroupsActiveEmployeesByOrgUnitAndGender_IncludingUnassigned()
    {
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var context = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            context.Employees.Add(NewActiveEmployee(tenantId, "A", "Unit", gender: Gender.Female, orgUnitId: unitId));
            context.Employees.Add(NewActiveEmployee(tenantId, "B", "Unit", gender: Gender.Female, orgUnitId: unitId));
            context.Employees.Add(NewActiveEmployee(tenantId, "C", "NoUnit", gender: Gender.Male, orgUnitId: null));
            await context.SaveChangesAsync();
        }

        var queries = NewQueries(context);
        IReadOnlyList<GenderByOrgUnitCount> counts;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            counts = await queries.GetGenderByOrgUnitAsync(tenantId, CancellationToken.None);
        }

        counts.Single(c => c.OrgUnitId == unitId).Count.ShouldBe(2);
        counts.Single(c => c.OrgUnitId == unitId).Gender.ShouldBe(nameof(Gender.Female));
        counts.Single(c => c.OrgUnitId == null).Count.ShouldBe(1);
    }

    /// <summary>
    /// senseik-046: two employees under different Title-level units of the same Department must land in one
    /// Department bucket (unlike GetGenderByOrgUnitAsync, which would keep them apart under their raw, Title-level
    /// OrgUnitId); an employee placed directly on the Department, and one with no Department ancestor at all
    /// (Company-level), are both handled too.
    /// </summary>
    [Fact]
    public async Task GetGenderByDepartmentAsync_RollsUpTitleLevelAssignments_OntoTheirDepartmentAncestor()
    {
        var tenantId = Guid.NewGuid();
        var companyType = Guid.NewGuid();
        var departmentType = Guid.NewGuid();
        var titleType = Guid.NewGuid();
        var company = Guid.NewGuid();
        var sales = Guid.NewGuid();
        var salesRepTitle = Guid.NewGuid();
        var salesLeadTitle = Guid.NewGuid();
        var context = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ListActiveTypesAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<OrgUnitTypeInfo>
        {
            new(companyType, "Company", "Şirket", 1),
            new(departmentType, "Department", "Departman", 2),
            new(titleType, "Title", "Ünvan", 3),
        });
        orgUnits.ListActiveUnitsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<OrgUnitInfo>
        {
            new(company, companyType, null, "Acme"),
            new(sales, departmentType, company, "Satış"),
            new(salesRepTitle, titleType, sales, "Satış Temsilcisi"),
            new(salesLeadTitle, titleType, sales, "Satış Lideri"),
        });

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            // Two different Title-level units, same Department ancestor - must roll up together.
            context.Employees.Add(NewActiveEmployee(tenantId, "A", "Rep", gender: Gender.Female, orgUnitId: salesRepTitle));
            context.Employees.Add(NewActiveEmployee(tenantId, "B", "Lead", gender: Gender.Female, orgUnitId: salesLeadTitle));
            // Placed directly on the Department: no Title level below it, still counts under Sales.
            context.Employees.Add(NewActiveEmployee(tenantId, "C", "DeptOnly", gender: Gender.Male, orgUnitId: sales));
            // Company-level: no Department ancestor at all.
            context.Employees.Add(NewActiveEmployee(tenantId, "D", "CompanyOnly", gender: Gender.Male, orgUnitId: company));
            await context.SaveChangesAsync();
        }

        var queries = NewQueries(context, orgUnits);
        IReadOnlyList<GenderByOrgUnitCount> counts;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            counts = await queries.GetGenderByDepartmentAsync(tenantId, CancellationToken.None);
        }

        counts.Single(c => c.OrgUnitId == sales && c.Gender == nameof(Gender.Female)).Count.ShouldBe(2);
        counts.Single(c => c.OrgUnitId == sales && c.Gender == nameof(Gender.Male)).Count.ShouldBe(1);
        counts.Single(c => c.OrgUnitId == null).Count.ShouldBe(1);
    }

    private static EmployeeAnalyticsQueries NewQueries(EmployeeDbContext context, IOrgUnitLookup? orgUnits = null) =>
        new(context, orgUnits ?? Substitute.For<IOrgUnitLookup>());

    private static EmployeeEntity NewActiveEmployee(
        Guid tenantId, string firstName, string lastName, DateOnly? hireDate = null, Gender gender = Gender.Unspecified, Guid? orgUnitId = null) =>
        EmployeeEntity.Create(tenantId, new EmployeeInfo(firstName, lastName, HireDate: hireDate ?? new DateOnly(2024, 1, 1), Gender: gender), orgUnitId: orgUnitId).Value;

    private static EmployeeEntity NewDraftEmployee(Guid tenantId, string firstName, string lastName) =>
        EmployeeEntity.Create(tenantId, new EmployeeInfo(firstName, lastName)).Value;
}
