using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Creates an employee record.</summary>
[RequiresPermission(EmployeePermissions.EmployeesCreateKey)]
public sealed record CreateEmployeeCommand(
    string FirstName,
    string LastName,
    string? NationalId,
    string? WorkEmail,
    string? PersonalEmail,
    string? WorkPhone,
    DateOnly? HireDate,
    DateOnly? BirthDate,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId) : ICommand<EmployeeDetailDto>
{
    public EmployeeInfo ToInfo() => new(FirstName, LastName, NationalId, WorkEmail, PersonalEmail, WorkPhone, HireDate, BirthDate);
}

/// <summary>Updates an employee record (same body as create); out-of-scope targets return 404 (IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record UpdateEmployeeCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string? NationalId,
    string? WorkEmail,
    string? PersonalEmail,
    string? WorkPhone,
    DateOnly? HireDate,
    DateOnly? BirthDate,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;

    public EmployeeInfo ToInfo() => new(FirstName, LastName, NationalId, WorkEmail, PersonalEmail, WorkPhone, HireDate, BirthDate);
}

/// <summary>Shared field rules for create/update (mirrors the domain checks so the client gets per-field 400s).</summary>
public static class EmployeeInfoRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, EmployeeInfo> info)
    {
        v.RuleFor(x => info(x).FirstName).NotEmpty().MaximumLength(EmployeeLimits.NameMaxLength).OverridePropertyName(nameof(EmployeeInfo.FirstName));
        v.RuleFor(x => info(x).LastName).NotEmpty().MaximumLength(EmployeeLimits.NameMaxLength).OverridePropertyName(nameof(EmployeeInfo.LastName));
        v.RuleFor(x => info(x).NationalId)
            .Must(id => string.IsNullOrWhiteSpace(id) || TcKimlik.IsValid(id))
            .WithMessage(EmployeeDomainErrors.NationalIdInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.NationalId));
        v.RuleFor(x => info(x).WorkEmail)
            .Must(e => string.IsNullOrWhiteSpace(e) || EmailAddress.IsValid(e.Trim()))
            .WithMessage(EmployeeDomainErrors.WorkEmailInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.WorkEmail));
        v.RuleFor(x => info(x).PersonalEmail)
            .Must(e => string.IsNullOrWhiteSpace(e) || EmailAddress.IsValid(e.Trim()))
            .WithMessage(EmployeeDomainErrors.PersonalEmailInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.PersonalEmail));
        v.RuleFor(x => info(x).WorkPhone).MaximumLength(EmployeeLimits.PhoneMaxLength).OverridePropertyName(nameof(EmployeeInfo.WorkPhone));
    }
}

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator() => EmployeeInfoRules.Apply(this, x => x.ToInfo());
}

public sealed class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        EmployeeInfoRules.Apply(this, x => x.ToInfo());
    }
}

/// <summary>Reference checks shared by create/update: org unit scope and existence, manager existence, national id uniqueness.</summary>
public sealed class EmployeeReferenceValidator(ITenantContext tenant, IEmployeeRepository employees, IOrgUnitLookup orgUnits, IDataScopeService dataScope)
{
    public async Task<Result> ValidateAsync(
        EmployeeInfo info, Guid? orgUnitId, Guid? managerEmployeeId, EmployeeEntity? existing, string permission, CancellationToken ct)
    {
        // A unit manager may only place employees into the units they are authorized for (checked before existence: no probing).
        if (existing is null || orgUnitId != existing.OrgUnitId)
        {
            var accessible = await dataScope.GetAccessibleOrgUnitIdsAsync(permission, ct).ConfigureAwait(false);
            if (accessible is not null && (orgUnitId is not { } target || !accessible.Contains(target)))
            {
                return Error.Forbidden(EmployeeErrors.OrgUnitOutOfScope);
            }
        }

        if (orgUnitId is { } unitId && unitId != existing?.OrgUnitId &&
            !await orgUnits.ExistsActiveAsync(tenant.TenantId, unitId, ct).ConfigureAwait(false))
        {
            return Error.NotFound(EmployeeErrors.OrgUnitNotFound);
        }

        if (managerEmployeeId is { } managerId && managerId != existing?.Id && managerId != existing?.ManagerEmployeeId &&
            !await employees.ExistsActiveAsync(managerId, ct).ConfigureAwait(false))
        {
            return Error.NotFound(EmployeeErrors.ManagerNotFound);
        }

        if (!string.IsNullOrWhiteSpace(info.NationalId) &&
            await employees.NationalIdTakenAsync(info.NationalId.Trim(), existing?.Id, ct).ConfigureAwait(false))
        {
            return Error.Conflict(EmployeeErrors.NationalIdTaken);
        }

        return Result.Success();
    }
}

public sealed class CreateEmployeeHandler(
    ITenantContext tenant,
    IEmployeeRepository employees,
    EmployeeReferenceValidator references,
    EmployeeDetailBuilder details) : ICommandHandler<CreateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var info = command.ToInfo();
        var checkedRefs = await references.ValidateAsync(info, command.OrgUnitId, command.ManagerEmployeeId, existing: null, EmployeePermissions.EmployeesCreateKey, cancellationToken);
        if (checkedRefs.IsFailure)
        {
            return checkedRefs.Error;
        }

        var result = EmployeeEntity.Create(tenant.TenantId, info, userId: null, command.OrgUnitId, command.ManagerEmployeeId);
        if (result.IsFailure)
        {
            return result.Error;
        }

        employees.Add(result.Value);
        return await details.BuildAsync(EmployeeRow.From(result.Value), cancellationToken);
    }
}

public sealed class UpdateEmployeeHandler(
    IEmployeeRepository employees,
    EmployeeReferenceValidator references,
    EmployeeDetailBuilder details) : ICommandHandler<UpdateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(UpdateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var info = command.ToInfo();

        var checkedRefs = await references.ValidateAsync(info, command.OrgUnitId, command.ManagerEmployeeId, employee, EmployeePermissions.EmployeesUpdateKey, cancellationToken);
        if (checkedRefs.IsFailure)
        {
            return checkedRefs.Error;
        }

        if (command.ManagerEmployeeId != employee.ManagerEmployeeId)
        {
            var managerMap = await employees.GetManagerMapAsync(cancellationToken);
            var assigned = employee.AssignManager(command.ManagerEmployeeId, managerMap);
            if (assigned.IsFailure)
            {
                return assigned.Error;
            }
        }

        var updated = employee.UpdateInfo(info);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        employee.AssignOrgUnit(command.OrgUnitId);
        return await details.BuildAsync(EmployeeRow.From(employee), cancellationToken);
    }
}

/// <summary>Assigns one manager to every employee of a grid selection; per-item outcome (self → Skipped, cycle → Failed).</summary>
[RequiresPermission(EmployeePermissions.EmployeesBulkUpdateKey)]
public sealed record BulkAssignManagerCommand(EmployeeSelection Selection, Guid ManagerEmployeeId) : ICommand<BulkOperationResult>;

public sealed class BulkAssignManagerValidator : AbstractValidator<BulkAssignManagerCommand>
{
    public BulkAssignManagerValidator()
    {
        RuleFor(x => x.Selection).NotNull();
        RuleFor(x => x.ManagerEmployeeId).NotEmpty();
    }
}

public sealed class BulkAssignManagerHandler(
    ITenantContext tenant,
    IEmployeeRepository employees,
    EmployeeSelectionResolver resolver) : ICommandHandler<BulkAssignManagerCommand, BulkOperationResult>
{
    public async Task<Result<BulkOperationResult>> Handle(BulkAssignManagerCommand command, CancellationToken cancellationToken)
    {
        var managerId = command.ManagerEmployeeId;
        if (!await employees.ExistsActiveAsync(managerId, cancellationToken))
        {
            return Error.NotFound(EmployeeErrors.ManagerNotFound);
        }

        var resolved = await resolver.ResolveAsync(tenant.TenantId, command.Selection, EmployeePermissions.EmployeesReadKey, cancellationToken);

        // Explicit ids that were dropped (unknown or out of scope) are reported as failed rather than silently ignored.
        IReadOnlyList<Guid> requested = resolved;
        if (command.Selection.EmployeeIds is { } explicitIds)
        {
            var excluded = (command.Selection.ExcludedEmployeeIds ?? []).ToHashSet();
            requested = explicitIds.Distinct().Where(id => !excluded.Contains(id)).ToList();
        }

        var loaded = (await employees.ListByIdsAsync(resolved, cancellationToken)).ToDictionary(e => e.Id);
        var managerMap = new Dictionary<Guid, Guid?>(await employees.GetManagerMapAsync(cancellationToken));

        var items = new List<BulkItemResult>(requested.Count);
        foreach (var id in requested)
        {
            if (!loaded.TryGetValue(id, out var employee))
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Failed, EmployeeErrors.EmployeeNotFound));
                continue;
            }

            if (id == managerId)
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Skipped, EmployeeDomainErrors.ManagerSelf));
                continue;
            }

            var assigned = employee.AssignManager(managerId, managerMap);
            if (assigned.IsFailure)
            {
                items.Add(new BulkItemResult(id, BulkItemOutcome.Failed, assigned.Error.Code));
                continue;
            }

            managerMap[id] = managerId;
            items.Add(new BulkItemResult(id, BulkItemOutcome.Succeeded, null));
        }

        return BulkOperationResult.From(requested.Count, items);
    }
}
