using FluentValidation;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Assets;

[RequiresPermission("assets.assets.manage")]
public sealed record CreateAssetCommand(
    Guid CategoryId, string Name, string? SerialNumber, string? Description,
    string? AssetTag = null, string? Brand = null, string? Model = null,
    DateOnly? PurchaseDate = null, decimal? PurchaseCost = null, DateOnly? WarrantyEndsOn = null) : ICommand<Guid>;

public sealed class CreateAssetValidator : AbstractValidator<CreateAssetCommand>
{
    public CreateAssetValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AssetsLimits.NameMaxLength);
        RuleFor(x => x.SerialNumber).MaximumLength(AssetsLimits.SerialNumberMaxLength).When(x => x.SerialNumber is not null);
        RuleFor(x => x.Description).MaximumLength(AssetsLimits.DescriptionMaxLength).When(x => x.Description is not null);
        RuleFor(x => x.AssetTag).MaximumLength(AssetsLimits.AssetTagMaxLength).When(x => x.AssetTag is not null);
        RuleFor(x => x.Brand).MaximumLength(AssetsLimits.BrandMaxLength).When(x => x.Brand is not null);
        RuleFor(x => x.Model).MaximumLength(AssetsLimits.ModelMaxLength).When(x => x.Model is not null);
        RuleFor(x => x.PurchaseCost).PrecisionScale(AssetsLimits.CostPrecision, AssetsLimits.CostScale, false).When(x => x.PurchaseCost is not null);
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

        var details = new AssetDetails(command.AssetTag, command.Brand, command.Model, command.PurchaseDate, command.PurchaseCost, command.WarrantyEndsOn);
        var created = Asset.Create(tenant.TenantId, command.CategoryId, command.Name, command.SerialNumber, command.Description, details);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (created.Value.AssetTag is { } tag && await assets.IsAssetTagTakenAsync(tag, null, cancellationToken))
        {
            return Error.Conflict(AssetsDomainErrors.TagInUse);
        }

        assets.Add(created.Value);
        return created.Value.Id;
    }
}

[RequiresPermission("assets.assets.manage")]
public sealed record UpdateAssetCommand(
    Guid AssetId, string Name, string? SerialNumber, string? Description,
    string? AssetTag = null, string? Brand = null, string? Model = null,
    DateOnly? PurchaseDate = null, decimal? PurchaseCost = null, DateOnly? WarrantyEndsOn = null,
    AssetCondition? Condition = null) : ICommand;

public sealed class UpdateAssetValidator : AbstractValidator<UpdateAssetCommand>
{
    public UpdateAssetValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AssetsLimits.NameMaxLength);
        RuleFor(x => x.SerialNumber).MaximumLength(AssetsLimits.SerialNumberMaxLength).When(x => x.SerialNumber is not null);
        RuleFor(x => x.Description).MaximumLength(AssetsLimits.DescriptionMaxLength).When(x => x.Description is not null);
        RuleFor(x => x.AssetTag).MaximumLength(AssetsLimits.AssetTagMaxLength).When(x => x.AssetTag is not null);
        RuleFor(x => x.Brand).MaximumLength(AssetsLimits.BrandMaxLength).When(x => x.Brand is not null);
        RuleFor(x => x.Model).MaximumLength(AssetsLimits.ModelMaxLength).When(x => x.Model is not null);
        RuleFor(x => x.PurchaseCost).PrecisionScale(AssetsLimits.CostPrecision, AssetsLimits.CostScale, false).When(x => x.PurchaseCost is not null);
        RuleFor(x => x.Condition).IsInEnum().When(x => x.Condition is not null);
    }
}

/// <summary>Replaces the editable fields (PUT semantics): künye fields not sent are cleared; a null Condition leaves the current one.</summary>
public sealed class UpdateAssetHandler(IAssetRepository assets) : ICommandHandler<UpdateAssetCommand>
{
    public async Task<Result> Handle(UpdateAssetCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var details = new AssetDetails(command.AssetTag, command.Brand, command.Model, command.PurchaseDate, command.PurchaseCost, command.WarrantyEndsOn);
        var updated = existing.Value.Update(command.Name, command.SerialNumber, command.Description, details, command.Condition);
        if (updated.IsFailure)
        {
            return updated;
        }

        // A failed command is not saved, so the (already applied) in-memory edit is discarded with the unit of work.
        return existing.Value.AssetTag is { } tag && await assets.IsAssetTagTakenAsync(tag, existing.Value.Id, cancellationToken)
            ? Error.Conflict(AssetsDomainErrors.TagInUse)
            : Result.Success();
    }
}

/// <summary>
/// Requests assigning an Unassigned asset to an employee: starts a Workflow "AssetAssignment" approval process
/// (TK-018) instead of assigning directly - the asset moves to PendingAssignment and only becomes Assigned once
/// approved (see AssetAssignmentApprovalHandlers). Documents reevaluates AssetHandoverForm off AssetAssigned, which
/// is now enqueued when the approval completes, not here.
/// </summary>
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

public sealed class AssignAssetHandler(ITenantContext tenant, IAssetRepository assets, IEmployeeSalaryLookup employeeDates, IWorkflowService workflow)
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

        var requested = asset.RequestAssignment(command.EmployeeId, command.IssuedAt);
        if (requested.IsFailure)
        {
            return requested;
        }

        // The receiving employee drives approver resolution (their manager approves), like SubmitLeaveRequest for the requester.
        var approval = await workflow.StartAsync(ApprovalRequestTypes.AssetAssignment, asset.Id, command.EmployeeId, null, null, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        asset.AttachApproval(approval.Value);
        return Result.Success();
    }
}

/// <summary>Returns an Assigned asset (recording its condition and an optional note on the ledger); writes AssetReturned to this module's own outbox.</summary>
[RequiresPermission("assets.assets.manage")]
public sealed record ReturnAssetCommand(Guid AssetId, DateOnly ReturnedAt, AssetCondition Condition = AssetCondition.Good, string? Note = null) : ICommand;

public sealed class ReturnAssetValidator : AbstractValidator<ReturnAssetCommand>
{
    public ReturnAssetValidator()
    {
        RuleFor(x => x.AssetId).NotEmpty();
        RuleFor(x => x.Condition).IsInEnum();
    }
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
        var returned = asset.Return(command.ReturnedAt, command.Condition, command.Note);
        if (returned.IsFailure)
        {
            return returned;
        }

        if (employeeId is { } id)
        {
            outbox.Enqueue(new AssetReturned(asset.TenantId, asset.Id, id, command.ReturnedAt, asset.Name));
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

/// <summary>Reports an Unassigned or Assigned asset lost; when held by an employee the open ledger row closes with EndReason=Lost.</summary>
[RequiresPermission("assets.assets.manage")]
public sealed record MarkAssetLostCommand(Guid AssetId, DateOnly OccurredOn, string? Note = null) : ICommand;

public sealed class MarkAssetLostValidator : AbstractValidator<MarkAssetLostCommand>
{
    public MarkAssetLostValidator() => RuleFor(x => x.AssetId).NotEmpty();
}

public sealed class MarkAssetLostHandler(IAssetRepository assets) : ICommandHandler<MarkAssetLostCommand>
{
    public async Task<Result> Handle(MarkAssetLostCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        return existing.Value.MarkLost(command.OccurredOn, command.Note);
    }
}

/// <summary>Self-service "teslim aldım": the employee holding the asset confirms receipt of the open assignment.</summary>
[RequiresPermission("assets.my.assets")]
[RequiresAccessSetting(AccessSettingKeys.HomeShowAssets)]
public sealed record AcknowledgeAssetCommand(Guid AssetId) : ICommand;

public sealed class AcknowledgeAssetValidator : AbstractValidator<AcknowledgeAssetCommand>
{
    public AcknowledgeAssetValidator() => RuleFor(x => x.AssetId).NotEmpty();
}

public sealed class AcknowledgeAssetHandler(ICurrentUser currentUser, IAssetRepository assets, TimeProvider clock)
    : ICommandHandler<AcknowledgeAssetCommand>
{
    public async Task<Result> Handle(AcknowledgeAssetCommand command, CancellationToken cancellationToken)
    {
        var existing = await assets.GetByIdAsync(command.AssetId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        // A user without an employee record can never be the assignee.
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.Forbidden(AssetsDomainErrors.NotAssignee);
        }

        return existing.Value.Acknowledge(employeeId, clock.GetUtcNow().UtcDateTime);
    }
}
