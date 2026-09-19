using FluentValidation;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Assets;

[RequiresPermission("assets.assets.manage")]
public sealed record CreateAssetCommand(Guid CategoryId, string Name, string? SerialNumber, string? Description) : ICommand<Guid>;

public sealed class CreateAssetValidator : AbstractValidator<CreateAssetCommand>
{
    public CreateAssetValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AssetsLimits.NameMaxLength);
        RuleFor(x => x.SerialNumber).MaximumLength(AssetsLimits.SerialNumberMaxLength).When(x => x.SerialNumber is not null);
        RuleFor(x => x.Description).MaximumLength(AssetsLimits.DescriptionMaxLength).When(x => x.Description is not null);
    }
}

public sealed class CreateAssetHandler(ITenantContext tenant, IAssetRepository assets, IAssetCategoryRepository categories)
    : ICommandHandler<CreateAssetCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAssetCommand command, CancellationToken cancellationToken)
    {
        var category = await categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var created = Asset.Create(tenant.TenantId, command.CategoryId, command.Name, command.SerialNumber, command.Description);
        if (created.IsFailure)
        {
            return created.Error;
        }

        assets.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("assets.assets.manage")]
public sealed record UpdateAssetCommand(Guid AssetId, string Name, string? SerialNumber, string? Description) : ICommand;

public sealed class UpdateAssetValidator : AbstractValidator<UpdateAssetCommand>
{
    public UpdateAssetValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AssetsLimits.NameMaxLength);
        RuleFor(x => x.SerialNumber).MaximumLength(AssetsLimits.SerialNumberMaxLength).When(x => x.SerialNumber is not null);
        RuleFor(x => x.Description).MaximumLength(AssetsLimits.DescriptionMaxLength).When(x => x.Description is not null);
    }
}

public sealed class UpdateAssetHandler(IAssetRepository assets) : ICommandHandler<UpdateAssetCommand>
{
    public async Task<Result> Handle(UpdateAssetCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        return existing.Value.Update(command.Name, command.SerialNumber, command.Description);
    }
}

/// <summary>Assigns an Unassigned asset to an employee; writes AssetAssigned to this module's own outbox (Documents reevaluates AssetHandoverForm from it).</summary>
[RequiresPermission("assets.assets.manage")]
public sealed record AssignAssetCommand(Guid AssetId, Guid EmployeeId, DateOnly IssuedAt) : ICommand;

public sealed class AssignAssetValidator : AbstractValidator<AssignAssetCommand>
{
    public AssignAssetValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}

public sealed class AssignAssetHandler(
    ITenantContext tenant, IAssetRepository assets, IIntegrationEventOutbox outbox, IEmployeeSalaryLookup employeeDates)
    : ICommandHandler<AssignAssetCommand>
{
    public async Task<Result> Handle(AssignAssetCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        // The employee must exist in this tenant (dates == null) and the issue date cannot precede the hire date.
        var employment = await employeeDates.GetEmploymentDatesAsync(tenant.TenantId, command.EmployeeId, cancellationToken);
        if (employment is null)
        {
            return Error.NotFound(AssetsErrors.EmployeeNotFound);
        }

        if (employment.HireDate is { } hireDate && command.IssuedAt < hireDate)
        {
            return Error.Validation(AssetsErrors.IssuedBeforeHire);
        }

        var asset = existing.Value;
        if (!string.IsNullOrWhiteSpace(asset.SerialNumber)
            && await assets.IsSerialNumberAssignedElsewhereAsync(asset.SerialNumber, asset.Id, cancellationToken))
        {
            return Error.Conflict(AssetsDomainErrors.SerialNumberInUse);
        }

        var assigned = asset.Assign(command.EmployeeId, command.IssuedAt);
        if (assigned.IsFailure)
        {
            return assigned;
        }

        outbox.Enqueue(new AssetAssigned(asset.TenantId, asset.Id, command.EmployeeId, command.IssuedAt));
        return Result.Success();
    }
}

/// <summary>Returns an Assigned asset; writes AssetReturned to this module's own outbox.</summary>
[RequiresPermission("assets.assets.manage")]
public sealed record ReturnAssetCommand(Guid AssetId, DateOnly ReturnedAt) : ICommand;

public sealed class ReturnAssetValidator : AbstractValidator<ReturnAssetCommand>
{
    public ReturnAssetValidator() => RuleFor(x => x.AssetId).NotEmpty();
}

public sealed class ReturnAssetHandler(IAssetRepository assets, IIntegrationEventOutbox outbox) : ICommandHandler<ReturnAssetCommand>
{
    public async Task<Result> Handle(ReturnAssetCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var asset = existing.Value;
        var employeeId = asset.AssignedEmployeeId;
        var returned = asset.Return(command.ReturnedAt);
        if (returned.IsFailure)
        {
            return returned;
        }

        if (employeeId is { } id)
        {
            outbox.Enqueue(new AssetReturned(asset.TenantId, asset.Id, id, command.ReturnedAt));
        }

        return Result.Success();
    }
}

[RequiresPermission("assets.assets.manage")]
public sealed record RetireAssetCommand(Guid AssetId) : ICommand;

public sealed class RetireAssetValidator : AbstractValidator<RetireAssetCommand>
{
    public RetireAssetValidator() => RuleFor(x => x.AssetId).NotEmpty();
}

public sealed class RetireAssetHandler(IAssetRepository assets) : ICommandHandler<RetireAssetCommand>
{
    public async Task<Result> Handle(RetireAssetCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        return existing.Value.Retire();
    }
}
