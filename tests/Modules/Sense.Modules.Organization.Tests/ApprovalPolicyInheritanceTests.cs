using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

public sealed class ApprovalPolicyInheritanceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Department = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();

    private static Dictionary<Guid, OrgUnitNode> Tree(bool branchActive = true) => new()
    {
        [Root] = new(Root, null, true),
        [Branch] = new(Branch, Root, branchActive),
        [Department] = new(Department, Branch, true),
        [Team] = new(Team, Department, true),
    };

    private static OrgUnitApprovalPolicyAssignment Assign(Guid unit, string type, Guid policy) =>
        OrgUnitApprovalPolicyAssignment.Create(Tenant, unit, type, policy).Value;

    [Fact]
    public void ActivePath_IsNearestFirst_SkipsInactiveUnits_AndIsEmptyForUnknownUnit()
    {
        ApprovalPolicyInheritance.ActivePath(Team, Tree()).ShouldBe([Team, Department, Branch, Root]);
        ApprovalPolicyInheritance.ActivePath(Team, Tree(branchActive: false)).ShouldBe([Team, Department, Root]);
        ApprovalPolicyInheritance.ActivePath(Guid.NewGuid(), Tree()).ShouldBeEmpty();
    }

    [Fact]
    public void ActivePath_StopsOnParentCycle()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var nodes = new Dictionary<Guid, OrgUnitNode> { [a] = new(a, b, true), [b] = new(b, a, true) };

        ApprovalPolicyInheritance.ActivePath(a, nodes).ShouldBe([a, b]);
    }

    [Fact]
    public void Resolve_ChildInheritsFromNearestAncestor_OwnAssignmentOverrides_PerRequestType()
    {
        var rootPolicy = Guid.NewGuid();
        var departmentPolicy = Guid.NewGuid();
        var teamOvertime = Guid.NewGuid();
        var assignments = new[]
        {
            Assign(Root, ApprovalRequestTypes.Leave, rootPolicy),
            Assign(Department, ApprovalRequestTypes.Leave, departmentPolicy),
            Assign(Team, ApprovalRequestTypes.Overtime, teamOvertime),
        };
        var path = ApprovalPolicyInheritance.ActivePath(Team, Tree());

        ApprovalPolicyInheritance.Resolve(path, assignments, ApprovalRequestTypes.Leave).ShouldBe(new EffectiveApprovalPolicy(departmentPolicy, Department));
        ApprovalPolicyInheritance.Resolve(path, assignments, ApprovalRequestTypes.Overtime).ShouldBe(new EffectiveApprovalPolicy(teamOvertime, Team));
        ApprovalPolicyInheritance.Resolve(path, assignments, ApprovalRequestTypes.Expense).ShouldBeNull();
        ApprovalPolicyInheritance.Resolve(ApprovalPolicyInheritance.ActivePath(Branch, Tree()), assignments, ApprovalRequestTypes.Leave)
            .ShouldBe(new EffectiveApprovalPolicy(rootPolicy, Root));
    }

    [Fact]
    public void Resolve_IgnoresAssignmentsOfInactiveAncestors()
    {
        var rootPolicy = Guid.NewGuid();
        var assignments = new[] { Assign(Root, ApprovalRequestTypes.Leave, rootPolicy), Assign(Branch, ApprovalRequestTypes.Leave, Guid.NewGuid()) };

        ApprovalPolicyInheritance.Resolve(ApprovalPolicyInheritance.ActivePath(Team, Tree(branchActive: false)), assignments, ApprovalRequestTypes.Leave)
            .ShouldBe(new EffectiveApprovalPolicy(rootPolicy, Root));
    }

    [Theory]
    [InlineData("Leave", true)]
    [InlineData("VisaDocument", true)]
    [InlineData("Expense2", true)]
    [InlineData("leave", false)]
    [InlineData("Visa-Document", false)]
    [InlineData("Izin ", false)]
    [InlineData("İzin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RequestType_MustBeAsciiPascalCaseIdentifier(string? requestType, bool valid)
    {
        OrgUnitApprovalPolicyAssignment.IsValidRequestType(requestType).ShouldBe(valid);
        ApprovalRequestTypes.IsWellFormed(requestType).ShouldBe(valid);
    }

    [Fact]
    public void RequestType_LongerThanLimit_IsRejected()
    {
        var tooLong = "A" + new string('b', OrganizationLimits.RequestTypeMaxLength);

        OrgUnitApprovalPolicyAssignment.Create(Tenant, Root, tooLong, Guid.NewGuid()).Error.Code.ShouldBe(OrganizationDomainErrors.ApprovalAssignmentRequestTypeInvalid);
        ApprovalRequestTypes.MaxLength.ShouldBe(OrganizationLimits.RequestTypeMaxLength);
    }

    [Fact]
    public void Reassign_ToSamePolicy_IsNoOp_OtherwiseRaisesEvent()
    {
        var policy = Guid.NewGuid();
        var assignment = Assign(Root, ApprovalRequestTypes.Advance, policy);
        assignment.ClearDomainEvents();

        assignment.Reassign(policy).Value.ShouldBeFalse();
        assignment.DomainEvents.ShouldBeEmpty();

        var next = Guid.NewGuid();
        assignment.Reassign(next).Value.ShouldBeTrue();
        assignment.ApprovalPolicyId.ShouldBe(next);
        assignment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrgUnitApprovalPolicyAssigned>();

        assignment.Reassign(Guid.Empty).Error.Code.ShouldBe(OrganizationDomainErrors.ApprovalAssignmentPolicyRequired);
    }
}
