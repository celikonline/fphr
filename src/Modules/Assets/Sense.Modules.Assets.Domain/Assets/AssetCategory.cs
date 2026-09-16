using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Domain.Assets;

/// <summary>
/// A tenant's equipment category (seeded per tenant: "Computer"/"Phone"/"Vehicle"/"Other" - mirrors how Expense's
/// ExpenseCategory is seeded per tenant). Unlike ExpenseCategory, Name is a plain free-text field (not a
/// localization resource key): the roadmap's "zimmet şablonu" scope treats the seed rows as editable tenant data
/// from the start, not a fixed catalog with translated display names.
/// </summary>
public sealed class AssetCategory : TenantAggregateRoot<Guid>, IAuditLogged
{
    private AssetCategory()
    {
    }

    private AssetCategory(Guid id, Guid tenantId, string code, string name)
        : base(id, tenantId)
    {
        Code = code;
        Name = name;
    }

    /// <summary>ASCII identifier, unique per tenant (e.g. "computer", "phone").</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name (tenant-editable, e.g. "Bilgisayar").</summary>
    public string Name { get; private set; } = string.Empty;

    public static Result<AssetCategory> Create(Guid tenantId, string code, string name, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation(AssetsDomainErrors.CategoryCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(AssetsDomainErrors.NameRequired);
        }

        return new AssetCategory(
            id ?? Guid.CreateVersion7(),
            tenantId,
            Guard.MaxLength(code.Trim(), AssetsLimits.CategoryCodeMaxLength),
            Guard.MaxLength(name.Trim(), AssetsLimits.CategoryNameMaxLength));
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(AssetsDomainErrors.NameRequired);
        }

        Name = Guard.MaxLength(name.Trim(), AssetsLimits.CategoryNameMaxLength);
        return Result.Success();
    }
}
