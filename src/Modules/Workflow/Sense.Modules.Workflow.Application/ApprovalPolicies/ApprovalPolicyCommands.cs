using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application.ApprovalPolicies;

/// <summary>Creates a named approval policy (Kolay İK "Yeni süreç ekle").</summary>
[RequiresPermission("workflow.policies.create")]
public sealed record CreatePolicyCommand(
    string RequestType, string Name, bool ShowCurrentApproverToRequester, IReadOnlyList<ApprovalStepInput> Steps) : ICommand<Guid>;

public sealed class CreatePolicyValidator : AbstractValidator<CreatePolicyCommand>
{
    public CreatePolicyValidator()
    {
        RuleFor(x => x.RequestType).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(WorkflowLimits.NameMaxLength);
        RuleFor(x => x.Steps).NotNull();
        RuleFor(x => x.Steps).SetValidator(new ApprovalStepInputListValidator()).When(x => x.Steps is not null);
    }
}

public sealed class CreatePolicyHandler(
    ITenantContext tenant, IApprovalPolicyRepository policies, IEmployeeLookup employeeLookup) : ICommandHandler<CreatePolicyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePolicyCommand command, CancellationToken cancellationToken)
    {
        var invalidApprover = await FindInvalidApproverAsync(tenant.TenantId, command.Steps, employeeLookup, cancellationToken);
        if (invalidApprover is not null)
        {
            return Error.Validation(WorkflowErrors.PolicyStepApproverInvalid, (nameof(ApprovalStepInput.ApproverEmployeeId), invalidApprover));
        }

        var created = ApprovalPolicy.Create(
            tenant.TenantId, command.RequestType, command.Name, command.ShowCurrentApproverToRequester, ToDomainSteps(command.Steps));
        if (created.IsFailure)
        {
            return created.Error;
        }

        policies.Add(created.Value);
        return created.Value.Id;
    }

    internal static async Task<Guid?> FindInvalidApproverAsync(
        Guid tenantId, IReadOnlyList<ApprovalStepInput> steps, IEmployeeLookup employeeLookup, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            if (!await employeeLookup.ExistsActiveAsync(tenantId, step.ApproverEmployeeId, cancellationToken))
            {
                return step.ApproverEmployeeId;
            }
        }

        return null;
    }

    internal static IReadOnlyList<(Guid ApproverEmployeeId, bool IsRequired)> ToDomainSteps(IReadOnlyList<ApprovalStepInput> steps) =>
        steps.Select(s => (s.ApproverEmployeeId, s.IsRequired)).ToList();
}

/// <summary>Renames/replaces the step list of/changes the visibility of an approval policy.</summary>
[RequiresPermission("workflow.policies.update")]
public sealed record UpdatePolicyCommand(
    Guid PolicyId, string Name, bool ShowCurrentApproverToRequester, IReadOnlyList<ApprovalStepInput> Steps) : ICommand;

public sealed class UpdatePolicyValidator : AbstractValidator<UpdatePolicyCommand>
{
    public UpdatePolicyValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(WorkflowLimits.NameMaxLength);
        RuleFor(x => x.Steps).NotNull();
        RuleFor(x => x.Steps).SetValidator(new ApprovalStepInputListValidator()).When(x => x.Steps is not null);
    }
}

public sealed class UpdatePolicyHandler(
    ITenantContext tenant, IApprovalPolicyRepository policies, IEmployeeLookup employeeLookup) : ICommandHandler<UpdatePolicyCommand>
{
    public async Task<Result> Handle(UpdatePolicyCommand command, CancellationToken cancellationToken)
    {
        var existing = await policies.GetByIdAsync(command.PolicyId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var invalidApprover = await CreatePolicyHandler.FindInvalidApproverAsync(tenant.TenantId, command.Steps, employeeLookup, cancellationToken);
        if (invalidApprover is not null)
        {
            return Error.Validation(WorkflowErrors.PolicyStepApproverInvalid, (nameof(ApprovalStepInput.ApproverEmployeeId), invalidApprover));
        }

        var policy = existing.Value;
        var renamed = policy.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        var replaced = policy.ReplaceSteps(CreatePolicyHandler.ToDomainSteps(command.Steps));
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        policy.SetVisibility(command.ShowCurrentApproverToRequester);
        return Result.Success();
    }
}

/// <summary>Deactivates (soft-deletes) an approval policy; in-flight requests already snapshotted its steps and are unaffected.</summary>
[RequiresPermission("workflow.policies.delete")]
public sealed record DeactivatePolicyCommand(Guid PolicyId) : ICommand;

public sealed class DeactivatePolicyValidator : AbstractValidator<DeactivatePolicyCommand>
{
    public DeactivatePolicyValidator() => RuleFor(x => x.PolicyId).NotEmpty();
}

public sealed class DeactivatePolicyHandler(IApprovalPolicyRepository policies) : ICommandHandler<DeactivatePolicyCommand>
{
    public async Task<Result> Handle(DeactivatePolicyCommand command, CancellationToken cancellationToken)
    {
        var existing = await policies.GetByIdAsync(command.PolicyId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Deactivate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Shared step-list shape rules for create/update: 1..MaxSteps entries, contiguous step numbers starting at 1.</summary>
internal sealed class ApprovalStepInputListValidator : AbstractValidator<IReadOnlyList<ApprovalStepInput>>
{
    public ApprovalStepInputListValidator()
    {
        RuleFor(x => x.Count).InclusiveBetween(1, WorkflowLimits.MaxSteps).WithMessage(WorkflowErrors.PolicyMaxStepsExceeded);
        RuleFor(x => x)
            .Must(steps => steps.Select((s, i) => s.StepNumber == i + 1).All(ok => ok))
            .WithMessage("Step numbers must be contiguous starting at 1.")
            .When(x => x.Count > 0);
    }
}
