namespace Sense.Modules.Workflow.Application.ApprovalPolicies;

/// <summary>One rung of a policy's approver chain, for read models.</summary>
public sealed record ApprovalPolicyStepDto(int StepNumber, Guid ApproverEmployeeId, bool IsRequired);

/// <summary>Read model for an <see cref="Sense.Modules.Workflow.Domain.ApprovalPolicies.ApprovalPolicy"/>.</summary>
public sealed record ApprovalPolicyDto(
    Guid Id,
    string RequestType,
    string Name,
    bool IsActive,
    bool ShowCurrentApproverToRequester,
    IReadOnlyList<ApprovalPolicyStepDto> Steps);
