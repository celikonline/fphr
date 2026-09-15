using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// Trivial, dependency-free smoke test proving the test project's references resolve (no DB, no DI container).
/// Real unit/integration tests land once the Workflow module's Application/Infrastructure land.
/// </summary>
public sealed class WorkflowModuleSmokeTests
{
    [Fact]
    public void Create_WithSingleValidStep_Succeeds()
    {
        var result = ApprovalPolicy.Create(
            tenantId: Guid.NewGuid(),
            requestType: "Leave",
            name: "Default Leave Policy",
            showCurrentApproverToRequester: true,
            steps: [(Guid.NewGuid(), true)]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Steps.Count.ShouldBe(1);
    }
}
