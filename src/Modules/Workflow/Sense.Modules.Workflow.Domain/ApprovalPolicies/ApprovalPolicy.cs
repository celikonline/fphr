using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Domain.ApprovalPolicies;

/// <summary>
/// A named, ordered approver chain for a request type (Kolay İK "Yeni süreç ekle"). Multiple policies may exist for
/// the same <see cref="RequestType"/> - Organization's OrgUnitApprovalPolicyAssignment (org-unit hierarchy, see
/// Organization.Contracts.IOrgApprovalPolicyResolver) decides which policy id applies to a given submission; Workflow
/// only stores and resolves policies by id, it does not enforce uniqueness per request type.
/// </summary>
public sealed class ApprovalPolicy : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<ApprovalPolicyStep> _steps = [];

    private ApprovalPolicy()
    {
    }

    private ApprovalPolicy(Guid id, Guid tenantId, string requestType, string name, bool showCurrentApproverToRequester)
        : base(id, tenantId)
    {
        RequestType = requestType;
        Name = name;
        ShowCurrentApproverToRequester = showCurrentApproverToRequester;
        IsActive = true;
    }

    /// <summary>ASCII PascalCase identifier, matches Organization.Contracts.ApprovalRequestTypes' convention ("Leave", "Advance"...).</summary>
    public string RequestType { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    /// <summary>"Onay sırasının kimde olduğunu görüntülesin" - exposed to the requester via the status query.</summary>
    public bool ShowCurrentApproverToRequester { get; private set; }

    public IReadOnlyList<ApprovalPolicyStep> Steps => _steps.AsReadOnly();

    public static bool IsValidRequestType(string? requestType) =>
        !string.IsNullOrEmpty(requestType)
        && requestType.Length <= WorkflowLimits.RequestTypeMaxLength
        && char.IsAsciiLetterUpper(requestType[0])
        && requestType.All(char.IsAsciiLetterOrDigit);

    public static Result<ApprovalPolicy> Create(
        Guid tenantId, string requestType, string name, bool showCurrentApproverToRequester,
        IReadOnlyList<(Guid ApproverEmployeeId, bool IsRequired)> steps, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(requestType))
        {
            return Error.Validation(WorkflowDomainErrors.PolicyRequestTypeRequired);
        }

        if (!IsValidRequestType(requestType))
        {
            return Error.Validation(WorkflowDomainErrors.PolicyRequestTypeInvalid);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(WorkflowDomainErrors.PolicyNameRequired);
        }

        var stepsValidation = ValidateStepInput(steps);
        if (stepsValidation is not null)
        {
            return stepsValidation;
        }

        var policy = new ApprovalPolicy(
            id ?? Guid.CreateVersion7(), tenantId, requestType, Guard.MaxLength(name.Trim(), WorkflowLimits.NameMaxLength), showCurrentApproverToRequester);
        policy.ReplaceStepsUnchecked(steps);
        policy.Raise(new ApprovalPolicyChanged(policy.Id, tenantId, policy.RequestType));
        return policy;
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(WorkflowDomainErrors.PolicyNameRequired);
        }

        Name = Guard.MaxLength(name.Trim(), WorkflowLimits.NameMaxLength);
        return Result.Success();
    }

    public void SetVisibility(bool showCurrentApproverToRequester) => ShowCurrentApproverToRequester = showCurrentApproverToRequester;

    public Result ReplaceSteps(IReadOnlyList<(Guid ApproverEmployeeId, bool IsRequired)> steps)
    {
        var validation = ValidateStepInput(steps);
        if (validation is not null)
        {
            return validation;
        }

        ReplaceStepsUnchecked(steps);
        Raise(new ApprovalPolicyChanged(Id, TenantId, RequestType));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(WorkflowDomainErrors.PolicyAlreadyInactive);
        }

        IsActive = false;
        Raise(new ApprovalPolicyChanged(Id, TenantId, RequestType));
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    private void ReplaceStepsUnchecked(IReadOnlyList<(Guid ApproverEmployeeId, bool IsRequired)> steps)
    {
        _steps.Clear();
        for (var i = 0; i < steps.Count; i++)
        {
            _steps.Add(new ApprovalPolicyStep(Id, i + 1, steps[i].ApproverEmployeeId, steps[i].IsRequired));
        }
    }

    private static Error? ValidateStepInput(IReadOnlyList<(Guid ApproverEmployeeId, bool IsRequired)> steps)
    {
        if (steps.Count == 0)
        {
            return Error.Validation(WorkflowDomainErrors.PolicyStepsRequired);
        }

        if (steps.Count > WorkflowLimits.MaxSteps)
        {
            return Error.Validation(WorkflowDomainErrors.PolicyMaxStepsExceeded, (nameof(WorkflowLimits.MaxSteps), WorkflowLimits.MaxSteps));
        }

        if (steps.Any(s => s.ApproverEmployeeId == Guid.Empty))
        {
            return Error.Validation(WorkflowDomainErrors.PolicyStepApproverRequired);
        }

        return null;
    }
}

/// <summary>One rung of an <see cref="ApprovalPolicy"/>'s chain, 1-based and contiguous within the policy.</summary>
public sealed class ApprovalPolicyStep : AuditableRecord
{
    private ApprovalPolicyStep()
    {
    }

    public ApprovalPolicyStep(Guid policyId, int stepNumber, Guid approverEmployeeId, bool isRequired)
    {
        PolicyId = policyId;
        StepNumber = stepNumber;
        ApproverEmployeeId = approverEmployeeId;
        IsRequired = isRequired;
    }

    public Guid PolicyId { get; private set; }

    public int StepNumber { get; private set; }

    public Guid ApproverEmployeeId { get; private set; }

    /// <summary>A reject on a required step terminates the request; a reject on an optional step is recorded but the request still advances.</summary>
    public bool IsRequired { get; private set; }
}

/// <summary>Raised on create/step-replace/activation changes; no subscriber yet (resolve-caching is a future item, see plan §9 kapsam dışı).</summary>
public sealed record ApprovalPolicyChanged(Guid PolicyId, Guid TenantId, string RequestType) : DomainEvent;
