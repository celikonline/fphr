using Sense.Modules.Organization.Application.OrgUnits;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>Unit tests for the in-memory tree-building logic (see OrgUnitTreeBuilder for why a flat-list + in-memory build was chosen over a recursive CTE).</summary>
public sealed class OrgUnitTreeBuilderTests
{
    private static OrgUnitDto Unit(Guid id, Guid typeId, Guid? parentId, string name, int sortOrder = 0) =>
        new(id, typeId, parentId, name, ManagerEmployeeId: null, LocationId: null, sortOrder, IsActive: true);

    [Fact]
    public void Build_WithNoUnits_ReturnsEmpty()
    {
        var tree = OrgUnitTreeBuilder.Build([]);

        tree.ShouldBeEmpty();
    }

    [Fact]
    public void Build_WithOnlyRootUnits_ReturnsFlatTreeWithNoChildren()
    {
        var typeId = Guid.NewGuid();
        var a = Unit(Guid.NewGuid(), typeId, null, "Company A", sortOrder: 2);
        var b = Unit(Guid.NewGuid(), typeId, null, "Company B", sortOrder: 1);

        var tree = OrgUnitTreeBuilder.Build([a, b]);

        tree.Count.ShouldBe(2);
        // Ordered by SortOrder within the same parent level.
        tree[0].Id.ShouldBe(b.Id);
        tree[1].Id.ShouldBe(a.Id);
        tree[0].Children.ShouldBeEmpty();
    }

    [Fact]
    public void Build_WithNestedUnits_NestsChildrenUnderCorrectParent()
    {
        var companyType = Guid.NewGuid();
        var branchType = Guid.NewGuid();
        var departmentType = Guid.NewGuid();

        var company = Unit(Guid.NewGuid(), companyType, null, "Headquarters");
        var branch = Unit(Guid.NewGuid(), branchType, company.Id, "Istanbul Branch");
        var department = Unit(Guid.NewGuid(), departmentType, branch.Id, "Engineering");

        var tree = OrgUnitTreeBuilder.Build([company, branch, department]);

        tree.Count.ShouldBe(1);
        var rootNode = tree[0];
        rootNode.Id.ShouldBe(company.Id);
        rootNode.Children.Count.ShouldBe(1);

        var branchNode = rootNode.Children[0];
        branchNode.Id.ShouldBe(branch.Id);
        branchNode.Children.Count.ShouldBe(1);
        branchNode.Children[0].Id.ShouldBe(department.Id);
        branchNode.Children[0].Children.ShouldBeEmpty();
    }

    [Fact]
    public void Build_OrdersSiblingsBySortOrderAtEveryLevel()
    {
        var typeId = Guid.NewGuid();
        var parent = Unit(Guid.NewGuid(), typeId, null, "Parent");
        var childZ = Unit(Guid.NewGuid(), typeId, parent.Id, "Z", sortOrder: 3);
        var childA = Unit(Guid.NewGuid(), typeId, parent.Id, "A", sortOrder: 1);
        var childM = Unit(Guid.NewGuid(), typeId, parent.Id, "M", sortOrder: 2);

        var tree = OrgUnitTreeBuilder.Build([parent, childZ, childA, childM]);

        var children = tree[0].Children;
        children.Select(c => c.Name).ShouldBe(["A", "M", "Z"]);
    }

    [Fact]
    public void Build_WithCorruptCyclicParentChain_DoesNotRecurseForever()
    {
        // Defensive case only: writes should never produce this, but a read path must stay safe.
        var typeId = Guid.NewGuid();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var a = Unit(aId, typeId, bId, "A");
        var b = Unit(bId, typeId, aId, "B");

        var tree = OrgUnitTreeBuilder.Build([a, b]);

        // Neither node has a null ParentId, so both are "orphaned" relative to the root and the call
        // must still terminate without a StackOverflowException / infinite loop.
        tree.ShouldBeEmpty();
    }

    [Fact]
    public void Build_ManagerAndLocationIds_ArePreservedOnTreeNodes()
    {
        var typeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var unit = new OrgUnitDto(Guid.NewGuid(), typeId, null, "Sales", managerId, locationId, 0, IsActive: true);

        var tree = OrgUnitTreeBuilder.Build([unit]);

        tree[0].ManagerEmployeeId.ShouldBe(managerId);
        tree[0].LocationId.ShouldBe(locationId);
    }
}
