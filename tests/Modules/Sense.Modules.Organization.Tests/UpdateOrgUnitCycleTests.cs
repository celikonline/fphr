using NSubstitute;
using Sense.Modules.Organization.Application;
using Sense.Modules.Organization.Application.OrgUnits;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Regression: <see cref="UpdateOrgUnitHandler"/> used to reject only <c>ParentId == Id</c>, so moving a unit
/// under one of its own descendants (HQ &gt; A &gt; B &gt; C, A.parent = C) was accepted and the subtree vanished
/// from the tree. The handler now walks from the new parent to the root.
/// </summary>
public sealed class UpdateOrgUnitCycleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static OrgUnit Unit(string name, Guid? parentId) => OrgUnit.Create(TenantId, TypeId, parentId, name).Value;

    private static IOrgUnitRepository Repo(params OrgUnit[] all)
    {
        var repo = Substitute.For<IOrgUnitRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.Arg<Guid>();
            var found = all.FirstOrDefault(u => u.Id == id);
            return Task.FromResult(found is null ? Result.Failure<OrgUnit>(Error.NotFound("test.missing")) : Result.Success(found));
        });
        return repo;
    }

    private static Task<Result<OrgUnitDto>> Move(IOrgUnitRepository repo, OrgUnit unit, Guid? newParent) =>
        new UpdateOrgUnitHandler(Tenant(), repo).Handle(
            new UpdateOrgUnitCommand(unit.Id, unit.Name, newParent, null, null, 0), CancellationToken.None);

    [Fact]
    public async Task Update_ParentIsSelf_Fails()
    {
        var a = Unit("A", null);
        var result = await Move(Repo(a), a, a.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.ParentCausesCycle);
    }

    [Fact]
    public async Task Update_ParentIsDirectChild_Fails()
    {
        var a = Unit("A", null);
        var b = Unit("B", a.Id);
        var result = await Move(Repo(a, b), a, b.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.ParentCausesCycle);
        a.ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task Update_ParentIsGrandchild_Fails()
    {
        var hq = Unit("HQ", null);
        var a = Unit("A", hq.Id);
        var b = Unit("B", a.Id);
        var result = await Move(Repo(hq, a, b), a, b.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.ParentCausesCycle);
        a.ParentId.ShouldBe(hq.Id);
    }

    [Fact]
    public async Task Update_ParentIsGreatGrandchild_Fails()
    {
        var hq = Unit("HQ", null);
        var a = Unit("A", hq.Id);
        var b = Unit("B", a.Id);
        var c = Unit("C", b.Id);
        var result = await Move(Repo(hq, a, b, c), a, c.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.ParentCausesCycle);
        a.ParentId.ShouldBe(hq.Id);
    }

    [Fact]
    public async Task Update_MoveToUnrelatedBranch_Succeeds()
    {
        var hq = Unit("HQ", null);
        var a = Unit("A", hq.Id);
        var b = Unit("B", a.Id);
        var x = Unit("X", hq.Id);
        var y = Unit("Y", x.Id);
        var result = await Move(Repo(hq, a, b, x, y), a, y.Id);

        result.IsSuccess.ShouldBeTrue();
        a.ParentId.ShouldBe(y.Id);
    }

    [Fact]
    public async Task Update_MoveToRoot_Succeeds()
    {
        var hq = Unit("HQ", null);
        var a = Unit("A", hq.Id);
        var result = await Move(Repo(hq, a), a, null);

        result.IsSuccess.ShouldBeTrue();
        a.ParentId.ShouldBeNull();
    }
}
