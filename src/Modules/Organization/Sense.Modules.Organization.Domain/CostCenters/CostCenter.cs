using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.CostCenters;

/// <summary>
/// A tenant-scoped cost center catalog entry (Faz 2 roadmap item #2, dar tasarım: "önce dar tasarla" -
/// this is the catalog only, not the headcount/roster planning that will sit on top of it in a later
/// slice). <see cref="Code"/> is set once at creation and never changes - Payroll/Analytics (S-4,
/// <c>ICostCenterLookup</c>) will key off it, and OrgUnit/Employee assignment (not yet wired - "başka
/// modüle bağlama" per TALIMAT tur 3) is expected to reference cost centers by <see cref="Id"/>, not code.
/// </summary>
public sealed class CostCenter : TenantAggregateRoot<Guid>, IAuditLogged
{
    private CostCenter()
    {
    }

    private CostCenter(Guid id, Guid tenantId, string code, string name)
        : base(id, tenantId)
    {
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Immutable once set (see class summary); short code Payroll/Analytics key off.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Result<CostCenter> Create(Guid tenantId, string code, string name, Guid? id = null)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode is null)
        {
            return Error.Validation(OrganizationDomainErrors.CostCenterCodeRequired);
        }

        if (normalizedCode.Length > OrganizationLimits.CostCenterCodeMaxLength)
        {
            return Error.Validation(OrganizationDomainErrors.CostCenterCodeTooLong);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.CostCenterNameRequired);
        }

        var costCenter = new CostCenter(
            id ?? Guid.CreateVersion7(),
            tenantId,
            normalizedCode,
            Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength));

        costCenter.Raise(new CostCenterCreated(costCenter.Id, tenantId, costCenter.Code, costCenter.Name));
        return costCenter;
    }

    /// <summary>Renames the cost center. Code cannot be changed once created (see class summary).</summary>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.CostCenterNameRequired);
        }

        Name = Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength);
        Raise(new CostCenterRenamed(Id, TenantId, Name));
        return Result.Success();
    }

    public Result SetActive(bool isActive)
    {
        if (isActive == IsActive)
        {
            return Error.Rule(isActive ? OrganizationDomainErrors.CostCenterAlreadyActive : OrganizationDomainErrors.CostCenterAlreadyInactive);
        }

        IsActive = isActive;
        Raise(isActive ? new CostCenterActivated(Id, TenantId) : new CostCenterDeactivated(Id, TenantId));
        return Result.Success();
    }

    private static string? NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}

public sealed record CostCenterCreated(Guid CostCenterId, Guid TenantId, string Code, string Name) : DomainEvent;

public sealed record CostCenterRenamed(Guid CostCenterId, Guid TenantId, string Name) : DomainEvent;

public sealed record CostCenterActivated(Guid CostCenterId, Guid TenantId) : DomainEvent;

public sealed record CostCenterDeactivated(Guid CostCenterId, Guid TenantId) : DomainEvent;
