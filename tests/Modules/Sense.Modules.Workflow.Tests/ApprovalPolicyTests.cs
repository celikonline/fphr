using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// Pure domain-level step-list shape rules. ApprovalPolicy.Create/ReplaceSteps take steps as an ordered
/// (ApproverEmployeeId, IsRequired) list with no caller-supplied StepNumber - step numbers are always assigned
/// contiguously from list order (see ApprovalPolicy.ReplaceStepsUnchecked), so only the MaxSteps limit is directly
/// testable there. Non-contiguous rejection is enforced where StepNumber IS caller-supplied: ApprovalRequest.Submit's
/// ApprovalTaskSeed list (WorkflowDomainErrors.PolicyStepsNotContiguous) - covered here for completeness.
/// </summary>
public sealed class ApprovalPolicyTests
{
    [Fact]
    public void Create_MoreThanMaxSteps_IsRejected()
    {
        var steps = Enumerable.Range(0, WorkflowLimits.MaxSteps + 1).Select(_ => (Guid.NewGuid(), true)).ToList();

        var result = ApprovalPolicy.Create(Guid.NewGuid(), "Leave", "Too Many Steps", true, steps);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.PolicyMaxStepsExceeded);
    }

    [Fact]
    public void ReplaceSteps_MoreThanMaxSteps_IsRejected()
    {
        var policy = ApprovalPolicy.Create(Guid.NewGuid(), "Leave", "Policy", true, [(Guid.NewGuid(), true)]).Value;
        var tooMany = Enumerable.Range(0, WorkflowLimits.MaxSteps + 1).Select(_ => (Guid.NewGuid(), true)).ToList();

        var result = policy.ReplaceSteps(tooMany);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.PolicyMaxStepsExceeded);

        // The rejected replacement must not have taken effect.
        policy.Steps.Count.ShouldBe(1);
    }

    [Fact]
    public void Submit_NonContiguousStepNumbers_IsRejected()
    {
        var steps = new[]
        {
            new ApprovalTaskSeed(1, Guid.NewGuid(), null, true),
            new ApprovalTaskSeed(3, Guid.NewGuid(), null, true),
        };

        var result = ApprovalRequest.Submit(
            Guid.NewGuid(), "Leave", Guid.NewGuid(), Guid.NewGuid(), steps,
            policyId: null, policyNameAtSubmission: null, resolvedFromOrgUnitId: null, resolvedByInheritance: false,
            showCurrentApproverToRequester: true, DateTime.UtcNow, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(WorkflowDomainErrors.PolicyStepsNotContiguous);
    }
}
