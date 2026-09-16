namespace Sense.Modules.Expense.Domain;

/// <summary>Domain error codes for Expense module.</summary>
public static class ExpenseDomainErrors
{
    public const string CategoryNotFound = "expense.category.not_found";
    public const string CategoryCodeRequired = "expense.category.code_required";
    public const string CategoryAlreadyInactive = "expense.category.already_inactive";
    public const string CategoryNameKeyRequired = "expense.category.name_key_required";

    public const string ClaimNotFound = "expense.claim.not_found";
    public const string ClaimNotDraft = "expense.claim.not_draft";
    public const string ClaimNotSubmitted = "expense.claim.not_submitted";
    public const string ClaimNoItems = "expense.claim.no_items";
    public const string ClaimNotApproved = "expense.claim.not_approved";

    public const string ItemNotFound = "expense.item.not_found";
    public const string ItemAmountInvalid = "expense.item.amount_invalid";
    public const string ItemTaxRateInvalid = "expense.item.tax_rate_invalid";
    public const string ItemCurrencyRequired = "expense.item.currency_required";

    public const string AdvanceNotFound = "expense.advance.not_found";
    public const string AdvanceNotSubmitted = "expense.advance.not_submitted";
    public const string AdvanceAmountInvalid = "expense.advance.amount_invalid";
    public const string AdvanceInstallmentCountInvalid = "expense.advance.installment_count_invalid";
    public const string AdvanceAlreadyPaid = "expense.advance.already_paid";
}

/// <summary>Domain constants and length limits.</summary>
public static class ExpenseLimits
{
    public const int DescriptionMaxLength = 1000;
    public const int CurrencyLength = 3;
    public const int CategoryCodeMaxLength = 100;
    public const int CategoryNameKeyMaxLength = 200;
    public const int FileKeyMaxLength = 500;

    /// <summary>Default upper bound for installment count; overridable later via TenantSettings.Expense.Advance.MaxInstallments (not yet implemented).</summary>
    public const int MaxInstallments = 12;
}
