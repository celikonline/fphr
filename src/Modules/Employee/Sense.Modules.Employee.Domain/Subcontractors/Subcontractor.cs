using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Modules.Employee.Domain.Subcontractors;

/// <summary>Editable content of a subcontractor company.</summary>
public sealed record SubcontractorData(
    string Name,
    string? TaxNumber = null,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    DateOnly? ContractStartDate = null,
    DateOnly? ContractEndDate = null);

/// <summary>
/// A subcontractor ("alt işveren / taşeron firma", 11.3.03 E5b): the company whose employees work at the tenant's
/// worksite. Tenant-scoped catalog, never hard-deleted (employees reference it) - deactivate instead. The İSG
/// headcount threshold that counts principal employer + subcontractor staff together is Faz 7 and reads this link.
/// </summary>
public sealed class Subcontractor : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Subcontractor(Guid id, Guid tenantId)
        : base(id, tenantId) => IsActive = true;

    private Subcontractor()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>10-digit tax number (VKN) or an 11-digit national id for a sole trader; free text, not validated.</summary>
    public string? TaxNumber { get; private set; }

    public string? ContactName { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public DateOnly? ContractStartDate { get; private set; }

    public DateOnly? ContractEndDate { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<Subcontractor> Create(Guid tenantId, SubcontractorData data, Guid? id = null)
    {
        var subcontractor = new Subcontractor(id ?? Guid.CreateVersion7(), tenantId);
        var applied = subcontractor.Update(data);
        return applied.IsFailure ? applied.Error : subcontractor;
    }

    public Result Update(SubcontractorData data)
    {
        var validated = Validate(data);
        if (validated.IsFailure)
        {
            return validated;
        }

        Name = data.Name.Trim();
        TaxNumber = Normalize(data.TaxNumber);
        ContactName = Normalize(data.ContactName);
        ContactEmail = Normalize(data.ContactEmail)?.ToLowerInvariant();
        ContactPhone = PhoneNormalizer.Normalize(data.ContactPhone);
        ContractStartDate = data.ContractStartDate;
        ContractEndDate = data.ContractEndDate;
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public static Result Validate(SubcontractorData data)
    {
        if (string.IsNullOrWhiteSpace(data.Name))
        {
            return Error.Validation(EmployeeDomainErrors.SubcontractorNameRequired);
        }

        if (data.Name.Trim().Length > SubcontractorLimits.NameMaxLength)
        {
            return Error.Validation(EmployeeDomainErrors.SubcontractorNameTooLong);
        }

        if (Normalize(data.TaxNumber) is { Length: > SubcontractorLimits.TaxNumberMaxLength })
        {
            return Error.Validation(EmployeeDomainErrors.SubcontractorTaxNumberTooLong);
        }

        if (Normalize(data.ContactEmail) is { } email && !EmailAddress.IsValid(email))
        {
            return Error.Validation(EmployeeDomainErrors.SubcontractorContactEmailInvalid);
        }

        if (data is { ContractStartDate: { } start, ContractEndDate: { } end } && end < start)
        {
            return Error.Validation(EmployeeDomainErrors.SubcontractorContractEndBeforeStart);
        }

        return Result.Success();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class SubcontractorLimits
{
    public const int NameMaxLength = 200;
    public const int TaxNumberMaxLength = 20;
    public const int ContactNameMaxLength = 150;
}
