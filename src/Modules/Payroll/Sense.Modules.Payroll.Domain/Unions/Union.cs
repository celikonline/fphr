using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Unions;

/// <summary>A trade union a tenant's employees may belong to (11.3.7 "Sendika / TİS": `Union{Name, IndustryBranchCode}`).
/// Deliberately minimal this slice - no collective-agreement (TİS) terms or retroactive-difference support yet
/// (faz3-durum-takibi.md #58: deferred, needs the not-yet-built Supplementary period status and a legal opinion on
/// term-comparison sufficiency, per 11.3.07-bordro.md's own "açık konular" note).</summary>
public sealed class Union : TenantAggregateRoot<Guid>
{
    private Union()
    {
    }

    private Union(Guid tenantId, string name, string? industryBranchCode)
        : base(Guid.NewGuid(), tenantId)
    {
        Name = name;
        IndustryBranchCode = industryBranchCode;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public string? IndustryBranchCode { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Union> Create(Guid tenantId, string name, string? industryBranchCode)
    {
        Guard.NotDefault(tenantId);
        var trimmedName = Trim(name);
        if (trimmedName is null or { Length: 0 })
        {
            return Error.Validation(PayrollDomainErrors.UnionNameRequired);
        }

        if (trimmedName.Length > PayrollLimits.UnionNameMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.UnionNameTooLong);
        }

        return new Union(tenantId, trimmedName, Trim(industryBranchCode));
    }

    public Result Update(string name, string? industryBranchCode)
    {
        var trimmedName = Trim(name);
        if (trimmedName is null or { Length: 0 })
        {
            return Error.Validation(PayrollDomainErrors.UnionNameRequired);
        }

        if (trimmedName.Length > PayrollLimits.UnionNameMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.UnionNameTooLong);
        }

        Name = trimmedName;
        IndustryBranchCode = Trim(industryBranchCode);
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
