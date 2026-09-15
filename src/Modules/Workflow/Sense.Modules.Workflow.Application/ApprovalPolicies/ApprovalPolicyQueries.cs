using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application.ApprovalPolicies;

/// <summary>Lists approval policies, optionally filtered to one request type.</summary>
[RequiresPermission("workflow.policies.read")]
public sealed record ListPoliciesQuery(string? RequestType) : IQuery<IReadOnlyList<ApprovalPolicyDto>>;

public sealed class ListPoliciesHandler(IApprovalPolicyRepository policies) : IQueryHandler<ListPoliciesQuery, IReadOnlyList<ApprovalPolicyDto>>
{
    public async Task<Result<IReadOnlyList<ApprovalPolicyDto>>> Handle(ListPoliciesQuery query, CancellationToken cancellationToken)
    {
        var list = await policies.ListAsync(query.RequestType, cancellationToken);
        IReadOnlyList<ApprovalPolicyDto> dtos = list.Select(ApprovalPolicyMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A single approval policy by id.</summary>
[RequiresPermission("workflow.policies.read")]
public sealed record GetPolicyQuery(Guid PolicyId) : IQuery<ApprovalPolicyDto>;

public sealed class GetPolicyHandler(IApprovalPolicyRepository policies) : IQueryHandler<GetPolicyQuery, ApprovalPolicyDto>
{
    public async Task<Result<ApprovalPolicyDto>> Handle(GetPolicyQuery query, CancellationToken cancellationToken)
    {
        var existing = await policies.GetByIdAsync(query.PolicyId, cancellationToken);
        return existing.IsFailure ? existing.Error : ApprovalPolicyMapper.ToDto(existing.Value);
    }
}

internal static class ApprovalPolicyMapper
{
    public static ApprovalPolicyDto ToDto(ApprovalPolicy policy) =>
        new(
            policy.Id,
            policy.RequestType,
            policy.Name,
            policy.IsActive,
            policy.ShowCurrentApproverToRequester,
            policy.Steps.Select(s => new ApprovalPolicyStepDto(s.StepNumber, s.ApproverEmployeeId, s.IsRequired)).ToList());
}
