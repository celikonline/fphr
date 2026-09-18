using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Terminations;

/// <summary>
/// Tenant-scoped termination reason catalog row ("İşten ayrılış nedeni", 11.3.03 E5a). Every tenant gets its own copy
/// of the system seed (<see cref="TerminationReasonSeed"/>) the first time it's needed, mirroring how Documents'
/// DocumentTypeDefinition is seeded per tenant. <see cref="DefaultSgkExitCode"/>/<see cref="DefaultTerminationType"/>
/// pre-fill the termination wizard's type/code fields; both are editable per-termination, so a null default here just
/// means the wizard leaves them for the user to pick. Tenants may add their own reasons and rename/deactivate any row
/// (their own copy of the seed included); <see cref="Code"/> and <see cref="IsSystem"/> stay immutable so an import or
/// a report can keep matching on the code. Rows are never deleted - terminated employees reference them.
/// </summary>
public sealed class TerminationReason : TenantAggregateRoot<Guid>
{
    private TerminationReason()
    {
    }

    private TerminationReason(Guid id, Guid tenantId, string code, bool isSystem)
        : base(id, tenantId)
    {
        Code = code;
        IsSystem = isSystem;
        IsActive = true;
    }

    /// <summary>Stable identifier (e.g. "Resignation"); immutable after creation; tenant-unique.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Pre-fills the wizard's SGK exit code field; system seed values are verified against SGK's official
    /// "İşten Ayrılış Nedenleri" table (senseik-006/F-01). Null for two system rows on purpose - see
    /// <see cref="TerminationReasonSeed"/>'s doc comment.
    /// </summary>
    public string? DefaultSgkExitCode { get; private set; }

    /// <summary>Pre-fills the wizard's termination type field; null when the reason doesn't imply one (e.g. Ölüm, Emeklilik).</summary>
    public TerminationType? DefaultTerminationType { get; private set; }

    /// <summary>True for seed rows, false for tenant-created reasons; immutable after creation.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int SortOrder { get; private set; }

    public static Result<TerminationReason> Create(
        Guid tenantId, string? code, string name, string? defaultSgkExitCode, TerminationType? defaultTerminationType, bool isSystem, int sortOrder, Guid? id = null)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (normalizedCode is null)
        {
            return Error.Validation(EmployeeDomainErrors.TerminationReasonCodeRequired);
        }

        if (normalizedCode.Length > TerminationReasonLimits.CodeMaxLength)
        {
            return Error.Validation(EmployeeDomainErrors.TerminationReasonCodeTooLong);
        }

        var trimmedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        if (trimmedName.Length == 0)
        {
            return Error.Validation(EmployeeDomainErrors.TerminationReasonNameRequired);
        }

        if (trimmedName.Length > TerminationReasonLimits.NameMaxLength)
        {
            return Error.Validation(EmployeeDomainErrors.TerminationReasonNameTooLong);
        }

        return new TerminationReason(id ?? Guid.CreateVersion7(), tenantId, normalizedCode, isSystem)
        {
            Name = trimmedName,
            DefaultSgkExitCode = string.IsNullOrWhiteSpace(defaultSgkExitCode) ? null : defaultSgkExitCode.Trim(),
            DefaultTerminationType = defaultTerminationType,
            SortOrder = sortOrder,
        };
    }

    /// <summary>Renames the reason and changes what it pre-fills; <see cref="Code"/> and <see cref="IsSystem"/> never change.</summary>
    public Result Update(string name, string? defaultSgkExitCode, TerminationType? defaultTerminationType, int sortOrder)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        if (trimmedName.Length == 0)
        {
            return Error.Validation(EmployeeDomainErrors.TerminationReasonNameRequired);
        }

        if (trimmedName.Length > TerminationReasonLimits.NameMaxLength)
        {
            return Error.Validation(EmployeeDomainErrors.TerminationReasonNameTooLong);
        }

        Name = trimmedName;
        DefaultSgkExitCode = string.IsNullOrWhiteSpace(defaultSgkExitCode) ? null : defaultSgkExitCode.Trim();
        DefaultTerminationType = defaultTerminationType;
        SortOrder = sortOrder;
        return Result.Success();
    }

    /// <summary>Hides the reason from the wizard's dropdown; already-terminated employees keep referencing it.</summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}

/// <summary>Length limits for termination reasons.</summary>
public static class TerminationReasonLimits
{
    public const int CodeMaxLength = 100;
    public const int NameMaxLength = 150;
    public const int SgkExitCodeMaxLength = 20;
}
