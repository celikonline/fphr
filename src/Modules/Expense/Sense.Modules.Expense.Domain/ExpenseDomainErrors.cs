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
    public const string ItemAmountTooLarge = "expense.item.amount_too_large";
    public const string ItemCurrencyUnsupported = "expense.item.currency_unsupported";
    public const string ItemReceiptDateInFuture = "expense.item.receipt_date_in_future";
    public const string ClaimCurrencyMismatch = "expense.claim.currency_mismatch";

    public const string AdvanceNotFound = "expense.advance.not_found";
    public const string AdvanceNotSubmitted = "expense.advance.not_submitted";
    public const string AdvanceAmountInvalid = "expense.advance.amount_invalid";
    public const string AdvanceAmountTooLarge = "expense.advance.amount_too_large";
    public const string AdvanceInstallmentCountInvalid = "expense.advance.installment_count_invalid";
    public const string AdvanceAlreadyPaid = "expense.advance.already_paid";
}

/// <summary>Domain constants and length limits.</summary>
public static class ExpenseLimits
{
    public const int DescriptionMaxLength = 1000;
    public const int CurrencyLength = 3;

    /// <summary>Upper bound for a single expense item/advance amount (sanity limit against typos such as 99999999999).</summary>
    public const decimal MaxAmount = 10_000_000m;

    /// <summary>A receipt may be dated at most this many days ahead of today (time-zone slack), never further.</summary>
    public const int MaxReceiptDateDaysAhead = 1;

    /// <summary>The payroll currency: only items in it are added to the payslip reimbursement.</summary>
    public const string PayrollCurrency = "TRY";

    /// <summary>ISO 4217 codes accepted on expense items and advances.</summary>
    public static readonly IReadOnlySet<string> AllowedCurrencies = new HashSet<string>(StringComparer.Ordinal)
    {
        "TRY", "USD", "EUR", "GBP", "CHF", "JPY", "CNY", "SAR", "AED", "RUB",
    };
    public const int CategoryCodeMaxLength = 100;
    public const int CategoryNameKeyMaxLength = 200;
    public const int FileKeyMaxLength = 500;

    /// <summary>Default upper bound for installment count; overridable later via TenantSettings.Expense.Advance.MaxInstallments (not yet implemented).</summary>
    public const int MaxInstallments = 12;
}
