using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.OrgUnitTypes;

/// <summary>Adds a tenant-custom hierarchy level right after an active level (null = at the end); active levels are renumbered 1..n.</summary>
[RequiresPermission(OrganizationPermissions.UnitTypesManageKey)]
public sealed record CreateOrgUnitTypeCommand(string Name, Guid? InsertAfterTypeId) : ICommand<OrgUnitTypeDto>;

public sealed class CreateOrgUnitTypeValidator : AbstractValidator<CreateOrgUnitTypeCommand>
{
    public CreateOrgUnitTypeValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
}

public sealed class CreateOrgUnitTypeHandler(ITenantContext tenant, IOrgUnitTypeRepository types) : ICommandHandler<CreateOrgUnitTypeCommand, OrgUnitTypeDto>
{
    public async Task<Result<OrgUnitTypeDto>> Handle(CreateOrgUnitTypeCommand command, CancellationToken cancellationToken)
    {
        var hierarchy = new OrgUnitTypeHierarchy(await types.ListAsync(activeOnly: false, cancellationToken));
        var created = hierarchy.AddCustom(tenant.TenantId, command.Name, command.InsertAfterTypeId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        types.Add(created.Value);
        return OrgUnitTypeMapping.ToDto(created.Value);
    }
}

/// <summary>Re-activates an inactive level (e.g. seeded Project/ApprovalUnit) right after an active level (null = at the end).</summary>
[RequiresPermission(OrganizationPermissions.UnitTypesManageKey)]
public sealed record ActivateOrgUnitTypeCommand(Guid Id, Guid? InsertAfterTypeId) : ICommand<OrgUnitTypeDto>;

public sealed class ActivateOrgUnitTypeValidator : AbstractValidator<ActivateOrgUnitTypeCommand>
{
    public ActivateOrgUnitTypeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class ActivateOrgUnitTypeHandler(IOrgUnitTypeRepository types) : ICommandHandler<ActivateOrgUnitTypeCommand, OrgUnitTypeDto>
{
    public async Task<Result<OrgUnitTypeDto>> Handle(ActivateOrgUnitTypeCommand command, CancellationToken cancellationToken)
    {
        var hierarchy = new OrgUnitTypeHierarchy(await types.ListAsync(activeOnly: false, cancellationToken));
        var activated = hierarchy.Activate(command.Id, command.InsertAfterTypeId);
        return activated.IsFailure ? activated.Error : OrgUnitTypeMapping.ToDto(activated.Value);
    }
}

/// <summary>Renames a level (names are tenant-editable, including system levels).</summary>
[RequiresPermission(OrganizationPermissions.UnitTypesManageKey)]
public sealed record RenameOrgUnitTypeCommand(Guid Id, string Name) : ICommand<OrgUnitTypeDto>;

public sealed class RenameOrgUnitTypeValidator : AbstractValidator<RenameOrgUnitTypeCommand>
{
    public RenameOrgUnitTypeValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class RenameOrgUnitTypeHandler(IOrgUnitTypeRepository types) : ICommandHandler<RenameOrgUnitTypeCommand, OrgUnitTypeDto>
{
    public async Task<Result<OrgUnitTypeDto>> Handle(RenameOrgUnitTypeCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByIdAsync(command.Id, cancellationToken);
        if (type.IsFailure)
        {
            return type.Error;
        }

        var renamed = type.Value.Rename(command.Name);
        return renamed.IsFailure ? renamed.Error : OrgUnitTypeMapping.ToDto(type.Value);
    }
}

/// <summary>Reorders the active levels; the list must contain exactly the active level ids with Company first. Returns all levels.</summary>
[RequiresPermission(OrganizationPermissions.UnitTypesManageKey)]
public sealed record ReorderOrgUnitTypesCommand(IReadOnlyList<Guid> OrderedTypeIds) : ICommand<IReadOnlyList<OrgUnitTypeDto>>;

public sealed class ReorderOrgUnitTypesValidator : AbstractValidator<ReorderOrgUnitTypesCommand>
{
    public ReorderOrgUnitTypesValidator() => RuleFor(x => x.OrderedTypeIds).NotNull().NotEmpty();
}

public sealed class ReorderOrgUnitTypesHandler(IOrgUnitTypeRepository types) : ICommandHandler<ReorderOrgUnitTypesCommand, IReadOnlyList<OrgUnitTypeDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitTypeDto>>> Handle(ReorderOrgUnitTypesCommand command, CancellationToken cancellationToken)
    {
        var hierarchy = new OrgUnitTypeHierarchy(await types.ListAsync(activeOnly: false, cancellationToken));
        var reordered = hierarchy.Reorder(command.OrderedTypeIds);
        if (reordered.IsFailure)
        {
            return reordered.Error;
        }

        IReadOnlyList<OrgUnitTypeDto> dtos = hierarchy.Types.Select(OrgUnitTypeMapping.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>
/// Removes a level: Company is immutable (422); a level whose active units still hold active employees is in use (409);
/// otherwise the level and its active units are deactivated, and their active children move up to the nearest remaining ancestor.
/// </summary>
[RequiresPermission(OrganizationPermissions.UnitTypesManageKey)]
public sealed record DeleteOrgUnitTypeCommand(Guid Id) : ICommand;

public sealed class DeleteOrgUnitTypeValidator : AbstractValidator<DeleteOrgUnitTypeCommand>
{
    public DeleteOrgUnitTypeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeleteOrgUnitTypeHandler(
    ITenantContext tenant,
    IOrgUnitTypeRepository types,
    IOrgUnitRepository units,
    IEmployeeLookup employeeLookup) : ICommandHandler<DeleteOrgUnitTypeCommand>
{
    public async Task<Result> Handle(DeleteOrgUnitTypeCommand command, CancellationToken cancellationToken)
    {
        var all = await types.ListAsync(activeOnly: false, cancellationToken);
        var type = all.FirstOrDefault(t => t.Id == command.Id);
        if (type is null)
        {
            return Error.NotFound(OrganizationErrors.OrgUnitTypeNotFound);
        }

        if (type.IsCompanyLevel)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeSystemImmutable);
        }

        if (!type.IsActive)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeAlreadyInactive);
        }

        var activeUnits = await units.ListAllAsync(activeOnly: true, cancellationToken);
        var removed = activeUnits.Where(u => u.TypeId == type.Id).ToDictionary(u => u.Id);
        if (removed.Count > 0 && await employeeLookup.AnyActiveInOrgUnitsAsync(tenant.TenantId, removed.Keys, cancellationToken))
        {
            return Error.Conflict(OrganizationErrors.UnitTypeInUse);
        }

        var result = new OrgUnitTypeHierarchy(all).Remove(type.Id);
        if (result.IsFailure)
        {
            return result.Error;
        }

        foreach (var child in activeUnits.Where(u => !removed.ContainsKey(u.Id) && u.ParentId is { } p && removed.ContainsKey(p)))
        {
            var parentId = child.ParentId;
            var guard = 0;
            while (parentId is { } id && removed.TryGetValue(id, out var removedParent) && guard++ < removed.Count)
            {
                parentId = removedParent.ParentId;
            }

            child.SetParent(parentId);
        }

        foreach (var unit in removed.Values)
        {
            unit.Deactivate();
        }

        return Result.Success();
    }
}

internal static class OrgUnitTypeMapping
{
    public static OrgUnitTypeDto ToDto(OrgUnitType t) => new(t.Id, t.Code, t.Name, t.SortOrder, t.IsActive, t.IsSystem, t.AllowsNesting);
}
