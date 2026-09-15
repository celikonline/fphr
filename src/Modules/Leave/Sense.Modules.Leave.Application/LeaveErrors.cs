namespace Sense.Modules.Leave.Application;

/// <summary>Application layer error codes (localized in SharedResource.resx).</summary>
public static class LeaveErrors
{
    public const string ProfileNotFound = "leave.profile.not_found";
    public const string EmployeeNotInScope = "leave.employee.not_in_scope";
    public const string NoEmployeeForUser = "leave.employee.not_linked";
    public const string TypeHasNoBalance = "leave.type.no_balance";
    public const string RuleSetNotFound = "leave.rule_set.not_found";
    public const string TypeNotFound = "leave.type.not_found";
    public const string InvalidRange = "leave.errors.invalid_range";
    public const string OverridePayableNotAllowed = "leave.errors.overtime_override_payable_not_allowed";
}
