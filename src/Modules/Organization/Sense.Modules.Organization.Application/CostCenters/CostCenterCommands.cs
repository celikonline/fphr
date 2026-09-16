using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.CostCenters;

/// <summary>
/// Creates a cost center catalog entry (S-4, roadmap Faz 2 item #2). <c>Code</c> is immutable once
/// created (see <see cref="CostCenter"/> class summary) - callers cannot rename it later, only the name.
/// </summary>
[RequiresPermission(OrganizationPermissions.CostCentersManageKey)]
public sealed record CreateCostCenterCommand(string Code, string Name) : ICommand<CreateCostCenterResult>;

public sealed class CreateCostCenterValidator : AbstractValidator<CreateCostCenterCommand>
{
    public CreateCostCenterValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(OrganizationLimits.CostCenterCodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class CreateCostCenterHandler(ITenantContext tenant, ICostCenterRepository costCenters)
    : ICommandHandler<CreateCostCenterCommand, CreateCostCenterResult>
{
    public async Task<Result<CreateCostCenterResult>> Handle(CreateCostCenterCommand command, CancellationToken cancellationToken)
    {
        // Uppercase-normalized: CostCenter.Create normalizes Code the same way (see its class summary), so
        // the pre-check must match on the normalized value too - otherwise "ik-01" would slip past this
        // check while "IK-01" already exists, and only fail later as an unhandled unique-index violation
        // once the repository is EF-backed (caught during this turn's manual review, no DB access needed).
        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        if (await costCenters.CodeExistsAsync(normalizedCode, cancellationToken))
        {
            return Error.Conflict(OrganizationDomainErrors.CostCenterCodeTaken);
        }

        var result = CostCenter.Create(tenant.TenantId, command.Code, command.Name);
        if (result.IsFailure)
        {
            return result.Error;
        }

        costCenters.Add(result.Value);
        return new CreateCostCenterResult(result.Value.Id);
    }
}

/// <summary>Renames a cost center; the code cannot be changed (see <see cref="CostCenter"/> class summary).</summary>
[RequiresPermission(OrganizationPermissions.CostCentersManageKey)]
public sealed record RenameCostCenterCommand(Guid Id, string Name) : ICommand;

public sealed class RenameCostCenterValidator : AbstractValidator<RenameCostCenterCommand>
{
    public RenameCostCenterValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class RenameCostCenterHandler(ICostCenterRepository costCenters) : ICommandHandler<RenameCostCenterCommand>
{
    public async Task<Result> Handle(RenameCostCenterCommand command, CancellationToken cancellationToken)
    {
        var existing = await costCenters.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Rename(command.Name);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>Activates or deactivates a cost center (S-4 "active" toggle uç).</summary>
[RequiresPermission(OrganizationPermissions.CostCentersManageKey)]
public sealed record SetCostCenterActiveCommand(Guid Id, bool IsActive) : ICommand;

public sealed class SetCostCenterActiveValidator : AbstractValidator<SetCostCenterActiveCommand>
{
    public SetCostCenterActiveValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SetCostCenterActiveHandler(ICostCenterRepository costCenters) : ICommandHandler<SetCostCenterActiveCommand>
{
    public async Task<Result> Handle(SetCostCenterActiveCommand command, CancellationToken cancellationToken)
    {
        var existing = await costCenters.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.SetActive(command.IsActive);
        return result.IsFailure ? result.Error : Result.Success();
    }
}
