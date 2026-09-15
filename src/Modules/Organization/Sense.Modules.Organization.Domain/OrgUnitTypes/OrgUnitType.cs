using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.OrgUnitTypes;

/// <summary>
/// A hierarchy level definition (e.g. Company, Branch, Department, Title). Tenants can reorder,
/// rename, and add custom levels (e.g. "Project") between existing ones instead of being locked into
/// a fixed Company/Branch/Department/Title chain (Faz 0 madde 4 / 11.3.02-sirket-yapisi.md: "Ünvan
/// bir seviyedir" - Title is a level, not a separate JobTitle entity). System levels (IsSystem) are
/// seeded once per tenant (see <see cref="OrgUnitTypeCatalog"/>) and the Company level specifically
/// can never be deactivated (a tenant must always have at least one active top-level unit type).
/// Ordering rules across the whole level set live in <see cref="OrgUnitTypeHierarchy"/>.
/// </summary>
public sealed class OrgUnitType : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OrgUnitType()
    {
    }

    private OrgUnitType(Guid id, Guid tenantId, string code, string name, int sortOrder, bool isSystem, bool allowsNesting)
        : base(id, tenantId)
    {
        Code = code;
        Name = name;
        SortOrder = sortOrder;
        IsSystem = isSystem;
        AllowsNesting = allowsNesting;
    }

    /// <summary>Stable machine code (e.g. "Company", "Department"); tenant-defined custom levels get a tenant-chosen code too.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name. Faz 0 keeps this a plain tenant-editable string rather than an i18n resource-key indirection.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Position in the hierarchy, top (Company) to bottom; tenant-editable via reorder (add level "+" between two others).</summary>
    public int SortOrder { get; private set; }

    /// <summary>Whether tenants can currently create units of this level. Off-by-default seed levels (Team/Project/ApprovalUnit) start false.</summary>
    public bool IsActive { get; private set; }

    /// <summary>System-seeded level (Company/Branch/Department/Title/Team/Project/ApprovalUnit); cannot be removed, only deactivated (Company cannot even be deactivated).</summary>
    public bool IsSystem { get; private set; }

    /// <summary>Whether units of this type may nest a unit of the same type under another (e.g. sub-projects). Reserved for later use; Faz 0 does not enforce it.</summary>
    public bool AllowsNesting { get; private set; }

    public static Result<OrgUnitType> Create(
        Guid tenantId,
        string code,
        string name,
        int sortOrder,
        bool isSystem,
        bool isActive = true,
        bool allowsNesting = false,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitTypeCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitTypeNameRequired);
        }

        var type = new OrgUnitType(
            id ?? Guid.CreateVersion7(),
            tenantId,
            Guard.MaxLength(code.Trim(), OrganizationLimits.CodeMaxLength),
            Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength),
            sortOrder,
            isSystem,
            allowsNesting)
        {
            IsActive = isActive,
        };

        type.Raise(new OrgUnitTypeAdded(type.Id, tenantId, type.Code, type.SortOrder));
        return type;
    }

    /// <summary>The system Company level: always active and always first.</summary>
    public bool IsCompanyLevel => IsSystem && string.Equals(Code, OrgUnitTypeCodes.Company, StringComparison.Ordinal);

    public Result Rename(string name)
    {
        var validated = ValidateName(name);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        Name = validated.Value;
        return Result.Success();
    }

    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    public void Activate() => IsActive = true;

    public Result Deactivate()
    {
        if (IsCompanyLevel)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeSystemImmutable);
        }

        if (!IsActive)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitTypeAlreadyInactive);
        }

        IsActive = false;
        Raise(new OrgUnitTypeRemoved(Id, TenantId));
        return Result.Success();
    }

    internal static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitTypeNameRequired);
        }

        var trimmed = name.Trim();
        return trimmed.Length > OrganizationLimits.NameMaxLength
            ? Error.Validation(OrganizationDomainErrors.OrgUnitTypeNameTooLong, ("max", OrganizationLimits.NameMaxLength))
            : trimmed;
    }
}

/// <summary>Well-known system level codes, seeded per tenant at tenant creation (see <see cref="OrgUnitTypeCatalog"/>).</summary>
public static class OrgUnitTypeCodes
{
    public const string Company = "Company";
    public const string Branch = "Branch";
    public const string Department = "Department";
    public const string Title = "Title";
    public const string Team = "Team";
    public const string Project = "Project";
    public const string ApprovalUnit = "ApprovalUnit";
}

public sealed record OrgUnitTypeAdded(Guid OrgUnitTypeId, Guid TenantId, string Code, int SortOrder) : DomainEvent;

public sealed record OrgUnitTypeRemoved(Guid OrgUnitTypeId, Guid TenantId) : DomainEvent;
