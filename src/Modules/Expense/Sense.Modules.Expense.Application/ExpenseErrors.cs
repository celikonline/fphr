namespace Sense.Modules.Expense.Application;

/// <summary>Application-layer error codes (handler-level checks not owned by the domain model).</summary>
public static class ExpenseErrors
{
    public const string NoEmployeeForUser = "expense.no_employee_for_user";
    public const string NotOwner = "expense.not_owner";
    public const string CategoryInactive = "expense.category.inactive";
}
