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

    /// <summary>
    /// KKD (senseik-015 Faz 4): items of this category are replaced every N days after issue (6331 md.30 / KKD
    /// Kullanımı Yönetmeliği - worn-out PPE must be renewed). Null = no periodic replacement.
    /// </summary>
    public int? ReplacementIntervalDays { get; private set; }

    public bool RequiresPeriodicReplacement => ReplacementIntervalDays is not null;

    public static Result<AssetCategory> Create(
        Guid tenantId, string code, string name, Guid? id = null, int? replacementIntervalDays = null)
    {
        if (!IsValidInterval(replacementIntervalDays))
        {
            return Error.Validation(AssetsDomainErrors.CategoryReplacementIntervalInvalid);
        }

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
            Guard.MaxLength(name.Trim(), AssetsLimits.CategoryNameMaxLength))
        {
            ReplacementIntervalDays = replacementIntervalDays,
        };
    }

    public Result SetReplacementInterval(int? days)
    {
        if (!IsValidInterval(days))
        {
            return Error.Validation(AssetsDomainErrors.CategoryReplacementIntervalInvalid);
        }

        ReplacementIntervalDays = days;
        return Result.Success();
    }

    /// <summary>When an item issued on <paramref name="issuedAt"/> is due for replacement; null without an interval.</summary>
    public DateOnly? ReplacementDueOn(DateOnly? issuedAt) =>
        issuedAt is { } issued && ReplacementIntervalDays is { } days ? issued.AddDays(days) : null;

    private static bool IsValidInterval(int? days) => days is null or (> 0 and <= AssetsLimits.MaxReplacementIntervalDays);

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
