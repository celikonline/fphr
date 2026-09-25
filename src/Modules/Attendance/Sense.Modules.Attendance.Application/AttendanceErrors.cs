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

    /// <summary>Zaman-devam Z0.3: recalculation range starts after it ends.</summary>
    public const string RecalculationRangeInvalid = "attendance.recalculation.range_invalid";

    /// <summary>Zaman-devam Z0.3: recalculation range longer than a year.</summary>
    public const string RecalculationRangeTooLong = "attendance.recalculation.range_too_long";

    /// <summary>Zaman-devam Z0.3: no recalculation job with this id for the tenant (or it was lost on restart).</summary>
    public const string RecalculationJobNotFound = "attendance.recalculation.job_not_found";

    /// <summary>Z2.1: the employee's month is closed (approved monthly timesheet, K6), so no missing-punch request can be raised for it.</summary>
    public const string PunchRequestPeriodLocked = "attendance.punch_request.period_locked";

    /// <summary>Z2.1: a pending request for the same employee/time/direction already exists.</summary>
    public const string PunchRequestDuplicate = "attendance.punch_request.duplicate";

    /// <summary>Z2.1: only the requesting employee can cancel a request.</summary>
    public const string PunchRequestNotOwner = "attendance.punch_request.not_owner";

    /// <summary>Z0.2: another shift definition of the tenant already uses this code.</summary>
    public const string ShiftDefinitionCodeDuplicate = "attendance.shift_definition.code_duplicate";

    /// <summary>Z0.2: a definition referenced by an assignment, rotation step or plan template cannot be deleted, only deactivated.</summary>
    public const string ShiftDefinitionInUse = "attendance.shift_definition.in_use";
}
