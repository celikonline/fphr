using Sense.Modules.Organization.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// <see cref="OrgUnitHierarchy"/> (Organization.Contracts) - the per-level "kariyer bilgileri" columns of the Raporlar
/// menu reports: explicit assignments win, ancestors of the deepest unit fill the gaps, unknown units are ignored.
/// </summary>
public sealed class OrgUnitHierarchyTests
{
    private static readonly OrgUnitTypeInfo Company = new(Guid.NewGuid(), "Company", "Şirket", 0);
    private static readonly OrgUnitTypeInfo Branch = new(Guid.NewGuid(), "Branch", "Şube", 1);
    private static readonly OrgUnitTypeInfo Department = new(Guid.NewGuid(), "Department", "Departman", 2);
    private static readonly OrgUnitTypeInfo Title = new(Guid.NewGuid(), "Title", "Unvan", 3);

    private static readonly OrgUnitInfo Acme = new(Guid.NewGuid(), Company.Id, null, "Acme A.Ş.");
    private static readonly OrgUnitInfo Istanbul = new(Guid.NewGuid(), Branch.Id, Acme.Id, "İstanbul");
    private static readonly OrgUnitInfo Engineering = new(Guid.NewGuid(), Department.Id, Istanbul.Id, "Yazılım");
    private static readonly OrgUnitInfo SeniorDev = new(Guid.NewGuid(), Title.Id, Engineering.Id, "Kıdemli Geliştirici");

    private static OrgUnitHierarchy Build() =>
        new([Title, Company, Department, Branch], [SeniorDev, Acme, Engineering, Istanbul]);

    [Fact]
    public void Levels_AreOrderedBySortOrder_CompanyFirst()
    {
        Build().Levels.Select(l => l.Code).ShouldBe(["Company", "Branch", "Department", "Title"]);
    }

    [Fact]
    public void LevelValues_FromDeepestUnitOnly_WalksParentsUpToTheCompany()
    {
        var values = Build().LevelValues(assignments: null, deepestUnitId: Engineering.Id);

        values.ShouldBe(["Acme A.Ş.", "İstanbul", "Yazılım", null]);
    }

    [Fact]
    public void LevelValues_ExplicitAssignmentWinsOverParentChain()
    {
        // Career record says the title is "Kıdemli Geliştirici" and the department is "Yazılım"; the denormalized unit is the title.
        var assignments = new Dictionary<Guid, Guid> { [Title.Id] = SeniorDev.Id, [Department.Id] = Engineering.Id };

        var values = Build().LevelValues(assignments, SeniorDev.Id);

        values.ShouldBe(["Acme A.Ş.", "İstanbul", "Yazılım", "Kıdemli Geliştirici"]);
    }

    [Fact]
    public void LevelValues_UnknownUnits_AreIgnoredNotThrown()
    {
        var assignments = new Dictionary<Guid, Guid> { [Department.Id] = Guid.NewGuid() };

        var values = Build().LevelValues(assignments, Guid.NewGuid());

        values.ShouldBe([null, null, null, null]);
    }

    [Fact]
    public void LevelValues_NoPlacement_YieldsBlankColumns()
    {
        Build().LevelValues(null, null).ShouldBe([null, null, null, null]);
        Build().UnitName(null).ShouldBeNull();
        Build().UnitName(Istanbul.Id).ShouldBe("İstanbul");
    }
}
