using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Expense.Domain.ExpenseClaims;

/// <summary>
/// A tenant's expense category (seeded per tenant, e.g. "expense.category.travel"/"expense.category.food" -
/// mirrors how Leave's LeaveType/LeaveRuleSet are seeded per tenant). <see cref="NameKey"/> is a resource key,
/// resolved by IErrorLocalizer-style lookup in the presentation layer; tenants may also give it a free-text override
/// later (not in this package's scope).
/// </summary>
public sealed class ExpenseCategory : TenantAggregateRoot<Guid>, IAuditLogged
{
    private ExpenseCategory()
    {
    }

    private ExpenseCategory(Guid id, Guid tenantId, string code, string nameKey, decimal? defaultTaxRate, decimal? maxAmount)
        : base(id, tenantId)
    {
        Code = code;
        NameKey = nameKey;
        DefaultTaxRate = defaultTaxRate;
        MaxAmount = maxAmount;
        IsActive = true;
    }

    /// <summary>ASCII identifier, unique per tenant (e.g. "travel", "food").</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Localization resource key (e.g. "expense.category.travel").</summary>
    public string NameKey { get; private set; } = string.Empty;

    /// <summary>Pre-selected VAT rate (%) for new items in this category; the user may still change it per item.</summary>
    public decimal? DefaultTaxRate { get; private set; }

    /// <summary>Optional per-item spending policy ceiling (TRY, tax-inclusive); null = no limit. Exceeding it only
    /// warns the submitter (Application layer's ExpenseItemDto.ExceedsCategoryLimit), it never blocks submission
    /// this slice.</summary>
    public decimal? MaxAmount { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<ExpenseCategory> Create(Guid tenantId, string code, string nameKey, decimal? defaultTaxRate, Guid? id = null, decimal? maxAmount = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation(ExpenseDomainErrors.CategoryCodeRequired);
        }

        return new ExpenseCategory(
            id ?? Guid.CreateVersion7(),
            tenantId,
            Guard.MaxLength(code.Trim(), ExpenseLimits.CategoryCodeMaxLength),
            Guard.MaxLength(nameKey.Trim(), ExpenseLimits.CategoryNameKeyMaxLength),
            defaultTaxRate,
            maxAmount);
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(ExpenseDomainErrors.CategoryAlreadyInactive);
        }

        IsActive = false;
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    /// <summary>Renames the category's display key (the "name" field the web UI shows - see PUT /expense/categories/{id}).
    /// The caller (<c>UpdateExpenseCategoryHandler</c>) is responsible for the system-category immutability rule
    /// (<c>ExpenseErrors.CategorySystemImmutable</c>); this method only enforces the basic non-empty/length invariant.</summary>
    public Result Rename(string nameKey)
    {
        if (string.IsNullOrWhiteSpace(nameKey))
        {
            return Error.Validation(ExpenseDomainErrors.CategoryNameKeyRequired);
        }

        NameKey = Guard.MaxLength(nameKey.Trim(), ExpenseLimits.CategoryNameKeyMaxLength);
        return Result.Success();
    }

    public void SetMaxAmount(decimal? maxAmount) => MaxAmount = maxAmount;
}
