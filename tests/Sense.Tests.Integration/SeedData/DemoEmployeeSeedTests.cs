using Sense.Api.SeedData;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Organization.Domain.OrgUnits;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.SeedData;

/// <summary>
/// H-04: before this fix every HR-role demo user (the fixed "ik@demo.local" account plus a couple of randomly
/// generated HR/HRSpecialist accounts) was routed to Headquarters and given the exact same
/// <see cref="SeedDataConstants.DemoHrTitle"/> title, so they showed up as indistinguishable siblings in the org
/// chart (e.g. "Ayşe Demir" vs "Deniz Balci"). Covers <see cref="DataSeeder.ResolveDemoOrgUnit"/> (unit routing)
/// and <see cref="DataSeeder.DemoTitleFor"/> (title text) together, the way <c>SeedDemoEmployees</c> calls them.
/// </summary>
public sealed class DemoEmployeeSeedTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CompanyTypeId = Guid.NewGuid();
    private static readonly Guid DepartmentTypeId = Guid.NewGuid();

    private static OrgUnit NewUnit(string name, Guid typeId, Guid? parentId = null) =>
        OrgUnit.Create(TenantId, typeId, parentId, name).Value;

    private static List<Role> RolesOf(params string[] codes) =>
        codes.Select(code => Role.CreateSystem(TenantId, code)).ToList();

    [Fact]
    public void ResolveDemoOrgUnit_SendsOwnerAndAdmin_ToHeadquarters()
    {
        var headquarters = NewUnit("Headquarters", CompanyTypeId);
        var departments = new[] { NewUnit("Sales", DepartmentTypeId, headquarters.Id) };
        var deptCursor = 0;

        var ownerUnit = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment: null, isExecutive: true, isHr: false, ref deptCursor);

        ownerUnit.ShouldBeSameAs(headquarters);
        deptCursor.ShouldBe(0);
    }

    [Fact]
    public void ResolveDemoOrgUnit_SendsOnlyTheFirstHrUser_ToHeadquarters_AndTheRestToTheHrDepartment()
    {
        var headquarters = NewUnit("Headquarters", CompanyTypeId);
        var hrDepartment = NewUnit("HR", DepartmentTypeId, headquarters.Id);
        var departments = new[] { hrDepartment };
        var deptCursor = 0;

        // First HR-role user: isExecutive=true (caller marks them as the HR director) -> Headquarters.
        var director = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment, isExecutive: true, isHr: true, ref deptCursor);
        // Every subsequent HR-role user: isExecutive=false -> routed to the real HR department, not Headquarters.
        var secondHr = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment, isExecutive: false, isHr: true, ref deptCursor);
        var thirdHr = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment, isExecutive: false, isHr: true, ref deptCursor);

        director.ShouldBeSameAs(headquarters);
        secondHr.ShouldBeSameAs(hrDepartment);
        thirdHr.ShouldBeSameAs(hrDepartment);
        // Routing HR overflow through the dedicated branch must not consume the round-robin department cursor.
        deptCursor.ShouldBe(0);
    }

    [Fact]
    public void ResolveDemoOrgUnit_RoundRobinsNonHrEmployees_AcrossDepartments()
    {
        var headquarters = NewUnit("Headquarters", CompanyTypeId);
        var sales = NewUnit("Sales", DepartmentTypeId, headquarters.Id);
        var engineering = NewUnit("Engineering", DepartmentTypeId, headquarters.Id);
        var departments = new[] { sales, engineering };
        var deptCursor = 0;

        var first = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment: null, isExecutive: false, isHr: false, ref deptCursor);
        var second = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment: null, isExecutive: false, isHr: false, ref deptCursor);
        var third = DataSeeder.ResolveDemoOrgUnit(headquarters, departments, hrDepartment: null, isExecutive: false, isHr: false, ref deptCursor);

        first.ShouldBeSameAs(sales);
        second.ShouldBeSameAs(engineering);
        third.ShouldBeSameAs(sales);
        deptCursor.ShouldBe(3);
    }

    [Fact]
    public void DemoTitleFor_GivesTheHrDirectorTitle_OnlyAtHeadquarters()
    {
        var headquarters = NewUnit("Headquarters", CompanyTypeId);
        var cursors = new Dictionary<Guid, int>();

        var title = DataSeeder.DemoTitleFor(headquarters, headquarters, RolesOf(SystemRoleCodes.HR), cursors);

        title.ShouldBe(SeedDataConstants.DemoHrTitle);
    }

    [Fact]
    public void DemoTitleFor_GivesOverflowHrUsers_DistinctDepartmentTitles_NotTheDirectorTitle()
    {
        var headquarters = NewUnit("Headquarters", CompanyTypeId);
        // DemoTitleFor looks up SeedDataConstants.DemoDepartments by unit.Name, so this unit's name must match
        // the (now Turkish) demo department name for the lookup below to resolve (senseik-043).
        var hrDepartment = NewUnit("İnsan Kaynakları", DepartmentTypeId, headquarters.Id);
        var cursors = new Dictionary<Guid, int>();
        var hrSpec = SeedDataConstants.DemoDepartments.Single(d => d.Name == "İnsan Kaynakları");

        var firstOverflowTitle = DataSeeder.DemoTitleFor(hrDepartment, headquarters, RolesOf(SystemRoleCodes.HR), cursors);
        var secondOverflowTitle = DataSeeder.DemoTitleFor(hrDepartment, headquarters, RolesOf(SystemRoleCodes.HR), cursors);

        firstOverflowTitle.ShouldBe(hrSpec.Titles[0]);
        secondOverflowTitle.ShouldBe(hrSpec.Titles[1 % hrSpec.Titles.Count]);
        firstOverflowTitle.ShouldNotBe(SeedDataConstants.DemoHrTitle);
        secondOverflowTitle.ShouldNotBe(SeedDataConstants.DemoHrTitle);
    }

    // senseik-043: demo Gender is derived from the first name so Reports -> Cinsiyet Dağılımı is meaningful.
    [Fact]
    public void DemoGenderPools_AreDisjoint_SoAPickedNameMapsToExactlyOneGender()
    {
        SeedDataConstants.TurkishMaleFirstNames.Intersect(SeedDataConstants.TurkishFemaleFirstNames, StringComparer.OrdinalIgnoreCase)
            .ShouldBeEmpty();
    }

    [Fact]
    public void DemoGenderFor_MapsEveryPoolNameToItsPoolsGender()
    {
        SeedDataConstants.TurkishFemaleFirstNames.ShouldAllBe(name => DataSeeder.DemoGenderFor(name) == Gender.Female);
        SeedDataConstants.TurkishMaleFirstNames.ShouldAllBe(name => DataSeeder.DemoGenderFor(name) == Gender.Male);
    }

    [Theory]
    [InlineData("Olena", Gender.Female)] // fixed foreign account, not in the Turkish pools
    [InlineData("Test", Gender.Male)] // fixed "Test Yaş" account falls back to Male
    public void DemoGenderFor_HandlesTheFixedAccountsOutsideThePools(string firstName, Gender expected)
    {
        DataSeeder.DemoGenderFor(firstName).ShouldBe(expected);
    }
}
