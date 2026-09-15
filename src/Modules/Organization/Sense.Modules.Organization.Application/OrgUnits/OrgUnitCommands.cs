using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.OrgUnits;

/// <summary>Creates an org unit at any hierarchy level (TypeId selects the level); a Company-level unit also gets its Company row.</summary>
[RequiresPermission("organization.units.create")]
public sealed record CreateOrgUnitCommand(
    Guid TypeId,
    Guid? ParentId,
    string Name,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    int SortOrder = 0) : ICommand<OrgUnitDto>;

public sealed class CreateOrgUnitValidator : AbstractValidator<CreateOrgUnitCommand>
{
    public CreateOrgUnitValidator()
    {
        RuleFor(x => x.TypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class CreateOrgUnitHandler(
    ITenantContext tenant,
    IOrgUnitRepository units,
    IOrgUnitTypeRepository types,
    ICompanyRepository companies,
    IEmployeeLookup? employeeLookup = null,
    ILocationLookup? locationLookup = null) : ICommandHandler<CreateOrgUnitCommand, OrgUnitDto>
{
    public async Task<Result<OrgUnitDto>> Handle(CreateOrgUnitCommand command, CancellationToken cancellationToken)
    {
        var type = await types.GetByIdAsync(command.TypeId, cancellationToken);
        if (type.IsFailure)
        {
            return Error.NotFound(OrganizationErrors.OrgUnitTypeNotFound);
        }

        if (!type.Value.IsActive)
        {
            return Error.Rule(OrganizationErrors.OrgUnitTypeInactive);
        }

        if (command.ParentId is { } parentId)
        {
            var parent = await units.GetByIdAsync(parentId, cancellationToken);
            if (parent.IsFailure)
            {
                return Error.NotFound(OrganizationErrors.ParentNotFound);
            }
        }

        if (command.ManagerEmployeeId is { } managerId && employeeLookup is not null &&
            !await employeeLookup.ExistsActiveAsync(tenant.TenantId, managerId, cancellationToken))
        {
            return Error.NotFound(OrganizationErrors.ManagerEmployeeNotFound);
        }

        if (command.LocationId is { } locationId && locationLookup is not null &&
            !await locationLookup.ExistsActiveAsync(tenant.TenantId, locationId, cancellationToken))
        {
            return Error.NotFound(OrganizationErrors.LocationReferenceNotFound);
        }

        var result = OrgUnit.Create(tenant.TenantId, command.TypeId, command.ParentId, command.Name, command.SortOrder);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var unit = result.Value;
        unit.AssignManager(command.ManagerEmployeeId);
        unit.AssignLocation(command.LocationId);

        units.Add(unit);

        if (type.Value.IsCompanyLevel)
        {
            var company = Company.Create(tenant.TenantId, unit.Id);
            if (company.IsFailure)
            {
                return company.Error;
            }

            companies.Add(company.Value);
        }

        return new OrgUnitDto(unit.Id, unit.TypeId, unit.ParentId, unit.Name, unit.ManagerEmployeeId, unit.LocationId, unit.SortOrder, unit.IsActive);
    }
}

/// <summary>Renames/reparents an org unit and (re)assigns its manager/location.</summary>
[RequiresPermission("organization.units.update")]
public sealed record UpdateOrgUnitCommand(
    Guid Id,
    string Name,
    Guid? ParentId,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    int SortOrder) : ICommand<OrgUnitDto>;

public sealed class UpdateOrgUnitValidator : AbstractValidator<UpdateOrgUnitCommand>
{
    public UpdateOrgUnitValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class UpdateOrgUnitHandler(
    ITenantContext tenant,
    IOrgUnitRepository units,
    IEmployeeLookup? employeeLookup = null,
    ILocationLookup? locationLookup = null) : ICommandHandler<UpdateOrgUnitCommand, OrgUnitDto>
{
    public async Task<Result<OrgUnitDto>> Handle(UpdateOrgUnitCommand command, CancellationToken cancellationToken)
    {
        var existing = await units.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (command.ParentId == command.Id)
        {
            return Error.Rule(OrganizationErrors.ParentCausesCycle);
        }

        if (command.ParentId is { } parentId)
        {
            var parent = await units.GetByIdAsync(parentId, cancellationToken);
            if (parent.IsFailure)
            {
                return Error.NotFound(OrganizationErrors.ParentNotFound);
            }
        }

        if (command.ManagerEmployeeId is { } managerId && employeeLookup is not null &&
            !await employeeLookup.ExistsActiveAsync(tenant.TenantId, managerId, cancellationToken))
        {
            return Error.NotFound(OrganizationErrors.ManagerEmployeeNotFound);
        }

        if (command.LocationId is { } locationId && locationLookup is not null &&
            !await locationLookup.ExistsActiveAsync(tenant.TenantId, locationId, cancellationToken))
        {
            return Error.NotFound(OrganizationErrors.LocationReferenceNotFound);
        }

        var unit = existing.Value;
        var renamed = unit.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        unit.SetParent(command.ParentId);
        unit.AssignManager(command.ManagerEmployeeId);
        unit.AssignLocation(command.LocationId);
        unit.Reorder(command.SortOrder);

        return new OrgUnitDto(unit.Id, unit.TypeId, unit.ParentId, unit.Name, unit.ManagerEmployeeId, unit.LocationId, unit.SortOrder, unit.IsActive);
    }
}

/// <summary>Soft-deletes an org unit (IsActive=false); fails with 409 if it still has active children.</summary>
[RequiresPermission("organization.units.delete")]
public sealed record DeactivateOrgUnitCommand(Guid Id) : ICommand;

public sealed class DeactivateOrgUnitValidator : AbstractValidator<DeactivateOrgUnitCommand>
{
    public DeactivateOrgUnitValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateOrgUnitHandler(IOrgUnitRepository units) : ICommandHandler<DeactivateOrgUnitCommand>
{
    public async Task<Result> Handle(DeactivateOrgUnitCommand command, CancellationToken cancellationToken)
    {
        var existing = await units.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (await units.HasChildrenAsync(command.Id, cancellationToken))
        {
            return Error.Conflict(OrganizationErrors.HasActiveChildren);
        }

        var result = existing.Value.Deactivate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}
