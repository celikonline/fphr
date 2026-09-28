namespace Sense.Modules.Attendance.Application;

/// <summary>Application-layer error codes (cross-module lookups; domain validation lives in AttendanceDomainErrors).</summary>
public static class AttendanceErrors
{
    public const string EmployeeNotFound = "attendance.employee.not_found";
    public const string EmployeeNotInScope = "attendance.employee.not_in_scope";
    public const string AvailabilityDateInPast = "attendance.availability.date_in_past";
    public const string DateRangeInvalid = "attendance.timesheet.date_range_invalid";

    /// <summary>A manual timesheet record cannot be entered for a day that has not started yet (tenant-local date).</summary>
    public const string DateInFuture = "attendance.timesheet.date_in_future";

    /// <summary>A monthly timesheet cannot be approved before its month has started (tenant-local date).</summary>
    public const string PeriodNotStarted = "attendance.timesheet.period_not_started";

    /// <summary>A monthly timesheet cannot be closed while its month is still running (tenant-local date).</summary>
    public const string MonthNotComplete = "attendance.timesheet.month_not_complete";

    /// <summary>S-31: no active Location has this QR self-servis check-in code for the tenant.</summary>
    public const string SelfCheckInInvalidCode = "attendance.self_checkin.invalid_code";

    /// <summary>S-31: the GPS check-in named a LocationId that doesn't resolve to an active tenant Location.</summary>
    public const string SelfCheckInLocationNotFound = "attendance.self_checkin.location_not_found";

    /// <summary>S-31: the location has a geofence radius but the request carried no (complete) coordinates.</summary>
    public const string SelfCheckInCoordinatesRequired = "attendance.self_checkin.coordinates_required";

    /// <summary>S-31: the caller's GPS coordinates are farther from the location's geofence center than its configured radius.</summary>
    public const string SelfCheckInOutsideGeofence = "attendance.self_checkin.outside_geofence";

    /// <summary>Mola başı: the caller has no open (checked in, not yet checked out) attendance day.</summary>
    public const string SelfCheckInBreakNotCheckedIn = "attendance.self_checkin.break_not_checked_in";

    /// <summary>Mola başı: a break was already started and not ended yet.</summary>
    public const string SelfCheckInBreakAlreadyOpen = "attendance.self_checkin.break_already_open";

    /// <summary>Mola sonu: there is no open break to end.</summary>
    public const string SelfCheckInBreakNotOpen = "attendance.self_checkin.break_not_open";

    /// <summary>FG-004: a punch correction cannot be requested for a day that has not started yet (tenant-local date).</summary>
    public const string CorrectionFutureDate = "attendance.correction.future_date";

    /// <summary>FG-004: the month is locked (ShiftPlanLock) or the employee's monthly timesheet is already approved.</summary>
    public const string CorrectionPeriodLocked = "attendance.correction.period_locked";

    /// <summary>FG-004: the employee already has a pending correction request for this day.</summary>
    public const string CorrectionPendingExists = "attendance.correction.pending_exists";

    /// <summary>FG-020: "Takvimim" self-servis günlük program sorgusu 42 günü aşamaz.</summary>
    public const string WorkScheduleRangeTooLong = "attendance.work_schedule.range_too_long";
}
