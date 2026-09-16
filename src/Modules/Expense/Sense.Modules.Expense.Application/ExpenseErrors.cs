namespace Sense.Modules.Expense.Application;

/// <summary>Application-layer error codes (handler-level checks not owned by the domain model).</summary>
public static class ExpenseErrors
{
    public const string NoEmployeeForUser = "expense.no_employee_for_user";
    public const string NotOwner = "expense.not_owner";
    public const string CategoryInactive = "expense.category.inactive";

    /// <summary>PUT /expense/categories/{id}: the seeded system catalog (<see cref="Categories.ExpenseCategorySeed"/>) may
    /// have its <c>MaxAmount</c> edited, but not its name - it is the localization key other places (e.g. the self-service
    /// claim form) resolve by <c>Code</c>, so a free-text rename would silently break those lookups.</summary>
    public const string CategorySystemImmutable = "expense.category.system_immutable";
}
