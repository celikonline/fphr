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
    public const string ResubmissionSourceInvalid = "leave.errors.resubmission_source_invalid";
    public const string CalendarRangeTooLarge = "leave.errors.calendar_range_too_large";
    public const string AbsenceNotFound = "leave.absence.not_found";

    /// <summary>G-5-09: an Unexcused/Excused absence cannot be recorded on a date already covered by an Approved full-day leave.</summary>
    public const string AbsenceConflictsWithLeave = "leave.absence.conflicts_with_leave";
}

/// <summary>Non-blocking warning codes (localized in SharedResource.resx; the API returns code + message).</summary>
public static class LeaveWarnings
{
    public const string UsageTypeMissingInRuleSet = "leave.warnings.usage_type_missing_in_rule_set";
}
