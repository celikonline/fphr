namespace Sense.Modules.Attendance.Domain;

/// <summary>Domain error codes for the Attendance module (localized in SharedResource.resx).</summary>
public static class AttendanceDomainErrors
{
    public const string ScheduleNotFound = "attendance.work_schedule.not_found";
    public const string ScheduleNameRequired = "attendance.work_schedule.name_required";
    public const string ScheduleDaysCountInvalid = "attendance.work_schedule.days_count_invalid";
    public const string ScheduleDayRangeInvalid = "attendance.work_schedule.day_range_invalid";
    public const string ScheduleDayBreakInvalid = "attendance.work_schedule.day_break_invalid";
    public const string ScheduleIsDefault = "attendance.errors.schedule_is_default";
    public const string ScheduleHasEmployees = "attendance.errors.schedule_has_employees";
    public const string ScheduleInactive = "attendance.errors.schedule_inactive";
    public const string AssignmentEffectiveDateInPast = "attendance.assignment.effective_date_in_past";
    public const string DayNotFound = "attendance.day.not_found";
    public const string DayCheckOutBeforeCheckIn = "attendance.day.check_out_before_check_in";
    public const string NoAnomalyToResolve = "attendance.day.no_anomaly_to_resolve";
    public const string DayAlreadyResolved = "attendance.day.already_resolved";
    public const string OverrideMinutesInvalid = "attendance.day.override_minutes_invalid";
    public const string AnomalyActionNotAllowed = "attendance.day.anomaly_action_not_allowed";
}

/// <summary>Domain limits.</summary>
public static class AttendanceLimits
{
    public const int NameMaxLength = 200;
    public const int DaysPerWeek = 7;
}
