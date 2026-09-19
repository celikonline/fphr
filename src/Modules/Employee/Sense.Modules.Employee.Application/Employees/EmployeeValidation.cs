using FluentValidation;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Terminations;
using Sense.Modules.Employee.Contracts;
// Not "using Sense.Modules.Employee.Contracts.Events" - it declares its own SuspensionReason/TerminationType
// (Contracts stays independent of Domain, per architecture rule), which would collide with Domain.Employees'
// versions below; event types (EmployeeHired/EmployeeSuspended/EmployeeReactivated/EmployeeTerminated) are
// referenced as Contracts.Events.* instead.
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

/// <summary>Shared field rules for create/update (mirrors the domain checks so the client gets per-field 400s).</summary>
public static class EmployeeInfoRules
{
    /// <summary>
    /// <paramref name="sensitiveAccess"/> gates the NationalId format rule: without employee.sensitive.view the
    /// caller only ever sees a masked value back, which the handler discards and replaces with the stored one
    /// (EmployeeSensitiveAccess.PreserveSensitiveFields) - validating that masked string against TcKimlik would
    /// otherwise reject a request that never touches NationalId.
    /// </summary>
    /// <param name="nationalityProvided">
    /// Update requests may leave Nationality unsent (the stored value is kept, senseik-058); the nationality-conditioned
    /// rules then cannot be judged from the request alone and are skipped here - Employee.Validate re-checks them
    /// against the merged info in the handler. Null = always provided (create).
    /// </param>
    public static void Apply<T>(AbstractValidator<T> v, Func<T, EmployeeInfo> info, EmployeeSensitiveAccess sensitiveAccess, Func<T, bool>? nationalityProvided = null)
    {
        bool NationalityKnown(T x) => nationalityProvided?.Invoke(x) ?? true;

        v.RuleFor(x => info(x).FirstName).NotEmpty().MaximumLength(EmployeeLimits.NameMaxLength).OverridePropertyName(nameof(EmployeeInfo.FirstName));
        v.RuleFor(x => info(x).LastName).NotEmpty().MaximumLength(EmployeeLimits.NameMaxLength).OverridePropertyName(nameof(EmployeeInfo.LastName));
        v.RuleFor(x => info(x).NationalId)
            .Must(id => string.IsNullOrWhiteSpace(id) || TcKimlik.IsValid(id))
            .WhenAsync((_, ct) => sensitiveAccess.CanViewAsync(ct))
            .WithMessage(EmployeeDomainErrors.NationalIdInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.NationalId));
        v.RuleFor(x => info(x).WorkEmail)
            .Must(e => string.IsNullOrWhiteSpace(e) || EmployeeEmailRules.IsValid(e.Trim()))
            .WithMessage(EmployeeDomainErrors.WorkEmailInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.WorkEmail));
        v.RuleFor(x => info(x).PersonalEmail)
            .Must(e => string.IsNullOrWhiteSpace(e) || EmployeeEmailRules.IsValid(e.Trim()))
            .WithMessage(EmployeeDomainErrors.PersonalEmailInvalid)
            .OverridePropertyName(nameof(EmployeeInfo.PersonalEmail));
        // Doğum tarihi gelecekte olamaz; doğum tarihinden sonra işe girilemez (senseik-044) - domain kuralının
        // FluentValidation aynası (alan bazlı 400 için).
        v.RuleFor(x => info(x).BirthDate)
            .Must(bd => bd is null || bd <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage(EmployeeDomainErrors.BirthDateInFuture)
            .OverridePropertyName(nameof(EmployeeInfo.BirthDate));
        v.RuleFor(x => info(x))
            .Must(i => i.BirthDate is null || i.HireDate is null || i.BirthDate <= i.HireDate)
            .WithMessage(EmployeeDomainErrors.BirthDateAfterHireDate)
            .OverridePropertyName(nameof(EmployeeInfo.BirthDate));
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

        v.RuleFor(x => info(x).SicilNumarasi).MaximumLength(EmployeeLimits.SicilNumarasiMaxLength).OverridePropertyName(nameof(EmployeeInfo.SicilNumarasi));
        v.RuleFor(x => info(x).ForeignIdNumber).MaximumLength(EmployeeLimits.ForeignIdNumberLength).OverridePropertyName(nameof(EmployeeInfo.ForeignIdNumber));
        v.RuleFor(x => info(x).PassportNumber).MaximumLength(EmployeeLimits.PassportNumberMaxLength).OverridePropertyName(nameof(EmployeeInfo.PassportNumber));
        v.RuleFor(x => info(x).SgkOccupationCode).MaximumLength(EmployeeLimits.SgkOccupationCodeMaxLength).OverridePropertyName(nameof(EmployeeInfo.SgkOccupationCode));

        // Mirrors Employee.Validate's nationality-conditioned NationalId format check (F-02); gated the same way as
        // the NationalId format rule above (masked NationalId never reaches here without the permission).
        v.RuleFor(x => info(x))
            .Must(i => string.IsNullOrWhiteSpace(i.NationalId) || !TcKimlik.IsValid(i.NationalId) ||
                       i.Nationality != Nationality.Turkish || !TcKimlik.Of(i.NationalId.Trim()).IsForeign)
            .WhenAsync(async (x, ct) => NationalityKnown(x) && await sensitiveAccess.CanViewAsync(ct))
            .WithMessage(EmployeeDomainErrors.NationalIdForeignPrefixForTurkish)
            .OverridePropertyName(nameof(EmployeeInfo.NationalId));
        v.RuleFor(x => info(x))
            .Must(i => string.IsNullOrWhiteSpace(i.NationalId) || !TcKimlik.IsValid(i.NationalId) ||
                       i.Nationality != Nationality.Foreign || TcKimlik.Of(i.NationalId.Trim()).IsForeign)
            .WhenAsync(async (x, ct) => NationalityKnown(x) && await sensitiveAccess.CanViewAsync(ct))
            .WithMessage(EmployeeDomainErrors.NationalIdNotForeignForForeignNational)
            .OverridePropertyName(nameof(EmployeeInfo.NationalId));

        // Not sensitive-view gated: ForeignIdNumber/PassportNumber are not masked like NationalId.
        v.RuleFor(x => info(x))
            .Must(i => i.Nationality != Nationality.Foreign || !string.IsNullOrWhiteSpace(i.ForeignIdNumber) || !string.IsNullOrWhiteSpace(i.PassportNumber))
            .When(NationalityKnown)
            .WithMessage(EmployeeDomainErrors.ForeignIdentificationRequired)
            .OverridePropertyName(nameof(EmployeeInfo.ForeignIdNumber));
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

        if (!string.IsNullOrWhiteSpace(info.SicilNumarasi) &&
            await employees.SicilNumarasiTakenAsync(info.SicilNumarasi.Trim(), existing?.Id, ct).ConfigureAwait(false))
        {
            return Error.Conflict(EmployeeErrors.SicilNumarasiTaken);
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
