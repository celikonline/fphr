using System.Threading;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Infrastructure.Resolvers;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// Unit tests for DefaultApproverResolver's resolution chain (org-unit policy -&gt; direct manager -&gt; fallback role
/// queue), with every port faked - no DB needed to exercise pure resolution logic.
/// </summary>
public sealed class DefaultApproverResolverTests
{
    private readonly IOrgApprovalPolicyResolver _orgPolicyResolver = Substitute.For<IOrgApprovalPolicyResolver>();
    private readonly IEmployeeLookup _employeeLookup = Substitute.For<IEmployeeLookup>();
    private readonly IApprovalPolicyRepository _policyRepository = Substitute.For<IApprovalPolicyRepository>();

    private DefaultApproverResolver Resolver => new(_orgPolicyResolver, _employeeLookup, _policyRepository);

    [Fact]
    public async Task ResolveAsync_ActiveOrgUnitPolicy_UsesItsSteps()
    {
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        var policy = ApprovalPolicy.Create(tenantId, "Leave", "Policy A", true, [(approverId, true)], id: policyId).Value;

        _employeeLookup.GetOrgUnitIdAsync(tenantId, requesterId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        _orgPolicyResolver.ResolveAsync(tenantId, orgUnitId, "Leave", Arg.Any<CancellationToken>())
            .Returns(new ApprovalPolicyResolution(policyId, orgUnitId, false));
        _policyRepository.FindByIdAsync(policyId, Arg.Any<CancellationToken>()).Returns(policy);

        var result = await Resolver.ResolveAsync(tenantId, "Leave", requesterId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PolicyId.ShouldBe(policyId);
        result.Value.PolicyName.ShouldBe("Policy A");
        result.Value.ResolvedFromOrgUnitId.ShouldBe(orgUnitId);
        result.Value.ResolvedByInheritance.ShouldBeFalse();
        var step = result.Value.Steps.ShouldHaveSingleItem();
        step.StepNumber.ShouldBe(1);
        step.ApproverEmployeeId.ShouldBe(approverId);
        step.FallbackRoleCode.ShouldBeNull();
        step.IsRequired.ShouldBeTrue();

        // The manager/role-queue fallback must not be consulted once an active policy resolved.
        await _employeeLookup.DidNotReceive().GetManagerEmployeeIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_NoOrgUnitPolicy_FallsBackToDirectManager()
    {
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        _employeeLookup.GetOrgUnitIdAsync(tenantId, requesterId, Arg.Any<CancellationToken>()).Returns(orgUnitId);
        _orgPolicyResolver.ResolveAsync(tenantId, orgUnitId, "Leave", Arg.Any<CancellationToken>()).Returns((ApprovalPolicyResolution?)null);
        _employeeLookup.GetManagerEmployeeIdAsync(tenantId, requesterId, Arg.Any<CancellationToken>()).Returns(managerId);

        var result = await Resolver.ResolveAsync(tenantId, "Leave", requesterId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PolicyId.ShouldBeNull();
        result.Value.PolicyName.ShouldBeNull();
        result.Value.ResolvedFromOrgUnitId.ShouldBeNull();
        result.Value.ResolvedByInheritance.ShouldBeFalse();
        var step = result.Value.Steps.ShouldHaveSingleItem();
        step.StepNumber.ShouldBe(1);
        step.ApproverEmployeeId.ShouldBe(managerId);
        step.FallbackRoleCode.ShouldBeNull();
        step.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task ResolveAsync_NoOrgUnitPolicyAndNoManager_FallsBackToHrRoleQueue()
    {
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();

        _employeeLookup.GetOrgUnitIdAsync(tenantId, requesterId, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _employeeLookup.GetManagerEmployeeIdAsync(tenantId, requesterId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await Resolver.ResolveAsync(tenantId, "Leave", requesterId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PolicyId.ShouldBeNull();
        var step = result.Value.Steps.ShouldHaveSingleItem();
        step.StepNumber.ShouldBe(1);
        step.ApproverEmployeeId.ShouldBeNull();
        step.FallbackRoleCode.ShouldBe("HR");
        step.IsRequired.ShouldBeTrue();

        // No org unit resolved for the employee - the org-unit policy resolver must never be consulted.
        await _orgPolicyResolver.DidNotReceive().ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
