using FluentValidation;
using Sense.Modules.Employee.Contracts;
// Not "using Sense.Modules.Employee.Contracts.Events" - it declares its own SuspensionReason (Contracts stays
// independent of Domain, per architecture rule), which would collide with Domain.Employees.SuspensionReason below;
// event types (EmployeeHired/EmployeeSuspended/EmployeeReactivated) are referenced as Contracts.Events.* instead.
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Bulk;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
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
    Guid? ManagerEmployeeId,
    Gender Gender = Gender.Unspecified,
    MaritalStatus MaritalStatus = MaritalStatus.Unspecified,
    int? ChildCount = null,
    EducationStatus? EducationStatus = null,
    HighestEducationLevel? HighestEducationLevel = null,
    MilitaryStatus? MilitaryStatus = null,
    DisabilityDegree? DisabilityDegree = null,
    BloodType? BloodType = null,
    string? MobilePhone = null,
    string? HomePhone = null,
    string? AddressLine = null,
    string? City = null,
    string? PostalCode = null,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null) : ICommand<EmployeeDetailDto>
{
    public EmployeeInfo ToInfo() => new(
        FirstName, LastName, NationalId, WorkEmail, PersonalEmail, WorkPhone, HireDate, BirthDate,
        Gender, MaritalStatus, ChildCount, EducationStatus, HighestEducationLevel, MilitaryStatus, DisabilityDegree, BloodType,
        MobilePhone, HomePhone, AddressLine, City, PostalCode, ContractType, EmploymentType);
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
    Guid? ManagerEmployeeId,
    Gender Gender = Gender.Unspecified,
    MaritalStatus MaritalStatus = MaritalStatus.Unspecified,
    int? ChildCount = null,
    EducationStatus? EducationStatus = null,
    HighestEducationLevel? HighestEducationLevel = null,
    MilitaryStatus? MilitaryStatus = null,
    DisabilityDegree? DisabilityDegree = null,
    BloodType? BloodType = null,
    string? MobilePhone = null,
    string? HomePhone = null,
    string? AddressLine = null,
    string? City = null,
    string? PostalCode = null,
    ContractType? ContractType = null,
    EmploymentType? EmploymentType = null) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;

    public EmployeeInfo ToInfo() => new(
        FirstName, LastName, NationalId, WorkEmail, PersonalEmail, WorkPhone, HireDate, BirthDate,
        Gender, MaritalStatus, ChildCount, EducationStatus, HighestEducationLevel, MilitaryStatus, DisabilityDegree, BloodType,
        MobilePhone, HomePhone, AddressLine, City, PostalCode, ContractType, EmploymentType);
}

/// <summary>Shared field rules for create/update (mirrors the domain checks so the client gets per-field 400s).</summary>
public static class EmployeeInfoRules
{
    /// <summary>
    /// <paramref name="sensitiveAccess"/> gates the NationalId format rule: without employee.sensitive.view the
    /// caller only ever sees a masked value back, which the handler discards and replaces with the stored one
    /// (EmployeeSensitiveAccess.PreserveSensitiveFields) - validating that masked string against TcKimlik would
    /// otherwise reject a request that never touches NationalId.
    /// </summary>
    public static void Apply<T>(AbstractValidator<T> v, Func<T, EmployeeInfo> info, EmployeeSensitiveAccess sensitiveAccess)
    {
        v.RuleFor(x => info(x).FirstName).NotEmpty().MaximumLength(EmployeeLimits.NameMaxLength).OverridePropertyName(nameof(EmployeeInfo.FirstName));
        v.RuleFor(x => info(x).LastName).NotEmpty().MaximumLength(EmployeeLimits.NameMaxLength).OverridePropertyName(nameof(EmployeeInfo.LastName));
        v.RuleFor(x => info(x).NationalId)
            .Must(id => string.IsNullOrWhiteSpace(id) || TcKimlik.IsValid(id))
            .WhenAsync((_, ct) => sensitiveAccess.CanViewAsync(ct))
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
        v.RuleFor(x => info(x).ChildCount).GreaterThanOrEqualTo(0)
            .When(x => info(x).ChildCount is not null)
            .WithMessage(EmployeeDomainErrors.ChildCountInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.ChildCount));
        v.RuleFor(x => PhoneNormalizer.Normalize(info(x).MobilePhone)).MaximumLength(EmployeeLimits.PhoneMaxLength)
            .WithMessage(EmployeeDomainErrors.MobilePhoneTooLong).OverridePropertyName(nameof(EmployeeInfo.MobilePhone));
        v.RuleFor(x => PhoneNormalizer.Normalize(info(x).HomePhone)).MaximumLength(EmployeeLimits.PhoneMaxLength)
            .WithMessage(EmployeeDomainErrors.HomePhoneTooLong).OverridePropertyName(nameof(EmployeeInfo.HomePhone));
        v.RuleFor(x => info(x).AddressLine).MaximumLength(EmployeeLimits.AddressLineMaxLength).OverridePropertyName(nameof(EmployeeInfo.AddressLine));
        v.RuleFor(x => info(x).City).MaximumLength(EmployeeLimits.CityMaxLength).OverridePropertyName(nameof(EmployeeInfo.City));
        v.RuleFor(x => info(x).PostalCode).MaximumLength(EmployeeLimits.PostalCodeMaxLength).OverridePropertyName(nameof(EmployeeInfo.PostalCode));
    }
}

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator(EmployeeSensitiveAccess sensitiveAccess) => EmployeeInfoRules.Apply(this, x => x.ToInfo(), sensitiveAccess);
}

public sealed class UpdateEmployeeValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeValidator(EmployeeSensitiveAccess sensitiveAccess)
    {
        RuleFor(x => x.Id).NotEmpty();
        EmployeeInfoRules.Apply(this, x => x.ToInfo(), sensitiveAccess);
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

/// <summary>employee.sensitive.view: gates TCKN (full vs masked) and health/disability fields (full vs null) everywhere they are read or written.</summary>
public sealed class EmployeeSensitiveAccess(ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<bool> CanViewAsync(CancellationToken ct) =>
        currentUser.UserId is { } userId && await permissions.HasAsync(userId, EmployeePermissions.SensitiveViewKey, ct).ConfigureAwait(false);

    /// <summary>Without the permission, a caller cannot set (or clear) what they cannot see: the current stored values win over whatever the request sent.</summary>
    public static EmployeeInfo PreserveSensitiveFields(EmployeeInfo info, EmployeeEntity? existing) => info with
    {
        NationalId = existing?.NationalId,
        MilitaryStatus = existing?.MilitaryStatus,
        DisabilityDegree = existing?.DisabilityDegree,
        BloodType = existing?.BloodType,
    };
}

public sealed class CreateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    EmployeeReferenceValidator references,
    EmployeeSensitiveAccess sensitiveAccess,
    IIntegrationEventOutbox outbox,
    EmployeeDetailBuilder details) : ICommandHandler<CreateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var info = command.ToInfo();
        if (!await sensitiveAccess.CanViewAsync(cancellationToken).ConfigureAwait(false))
        {
            info = EmployeeSensitiveAccess.PreserveSensitiveFields(info, existing: null);
        }

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

        var employee = result.Value;
        employees.Add(employee);

        if (employee.Status == EmployeeStatus.Active && employee.HireDate is { } hireDate)
        {
            outbox.Enqueue(new Contracts.Events.EmployeeHired(tenant.TenantId, employee.Id, hireDate, hireDate) { ActorUserId = currentUser.UserId });
        }

        return await details.BuildAsync(EmployeeRow.From(employee), cancellationToken);
    }
}

public sealed class UpdateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    EmployeeReferenceValidator references,
    EmployeeSensitiveAccess sensitiveAccess,
    IIntegrationEventOutbox outbox,
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
        if (!await sensitiveAccess.CanViewAsync(cancellationToken).ConfigureAwait(false))
        {
            info = EmployeeSensitiveAccess.PreserveSensitiveFields(info, employee);
        }

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

        if (updated.Value && employee.HireDate is { } hireDate)
        {
            outbox.Enqueue(new Contracts.Events.EmployeeHired(tenant.TenantId, employee.Id, hireDate, hireDate) { ActorUserId = currentUser.UserId });
        }

        return await details.BuildAsync(EmployeeRow.From(employee), cancellationToken);
    }
}

/// <summary>Suspends an Active employee (askıya alma); does not touch the Identity account.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record SuspendEmployeeCommand(Guid Id, SuspensionReason Reason) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class SuspendEmployeeValidator : AbstractValidator<SuspendEmployeeCommand>
{
    public SuspendEmployeeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SuspendEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IIntegrationEventOutbox outbox,
    TimeProvider clock,
    EmployeeDetailBuilder details) : ICommandHandler<SuspendEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(SuspendEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var suspended = employee.Suspend(command.Reason, clock.GetUtcNow().UtcDateTime);
        if (suspended.IsFailure)
        {
            return suspended.Error;
        }

        outbox.Enqueue(new Contracts.Events.EmployeeSuspended(tenant.TenantId, employee.Id, ToContractReason(command.Reason)) { ActorUserId = currentUser.UserId });
        return await details.BuildAsync(EmployeeRow.From(employee), cancellationToken);
    }

    private static Contracts.Events.SuspensionReason ToContractReason(SuspensionReason reason) => reason switch
    {
        SuspensionReason.MilitaryService => Contracts.Events.SuspensionReason.MilitaryService,
        SuspensionReason.UnionOffice => Contracts.Events.SuspensionReason.UnionOffice,
        SuspensionReason.LongUnpaidLeave => Contracts.Events.SuspensionReason.LongUnpaidLeave,
        SuspensionReason.Detention => Contracts.Events.SuspensionReason.Detention,
        SuspensionReason.HealthReport => Contracts.Events.SuspensionReason.HealthReport,
        SuspensionReason.ForceMajeure => Contracts.Events.SuspensionReason.ForceMajeure,
        _ => Contracts.Events.SuspensionReason.Other,
    };
}

/// <summary>Reactivates a Suspended employee back to Active.</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record ReactivateEmployeeCommand(Guid Id) : ICommand<EmployeeDetailDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class ReactivateEmployeeValidator : AbstractValidator<ReactivateEmployeeCommand>
{
    public ReactivateEmployeeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class ReactivateEmployeeHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IIntegrationEventOutbox outbox,
    EmployeeDetailBuilder details) : ICommandHandler<ReactivateEmployeeCommand, EmployeeDetailDto>
{
    public async Task<Result<EmployeeDetailDto>> Handle(ReactivateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var found = await employees.GetByIdAsync(command.Id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employee = found.Value;
        var reactivated = employee.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        outbox.Enqueue(new Contracts.Events.EmployeeReactivated(tenant.TenantId, employee.Id) { ActorUserId = currentUser.UserId });
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
