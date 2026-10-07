namespace Sense.Modules.Expense.Application;

/// <summary>Application-layer error codes (handler-level checks not owned by the domain model).</summary>
public static class ExpenseErrors
{
    public const string NoEmployeeForUser = "expense.no_employee_for_user";
    public const string NotOwner = "expense.not_owner";
    public const string CategoryInactive = "expense.category.inactive";

    /// <summary>ReceiptFileId is not an Active document of the caller (own upload or filed to the claim's employee). Payroll
    /// pays receipted items tax-free, so an unverifiable id must never land on an item.</summary>
    public const string ReceiptNotFound = "expense.item.receipt_not_found";
    public const string ReceiptTypeNotAllowed = "expense.item.receipt_type_not_allowed";

    /// <summary>PUT /expense/categories/{id}: the seeded system catalog (<see cref="Categories.ExpenseCategorySeed"/>) may
    /// have its <c>MaxAmount</c> edited, but not its name - it is the localization key other places (e.g. the self-service
    /// claim form) resolve by <c>Code</c>, so a free-text rename would silently break those lookups.</summary>
    public const string CategorySystemImmutable = "expense.category.system_immutable";
}
