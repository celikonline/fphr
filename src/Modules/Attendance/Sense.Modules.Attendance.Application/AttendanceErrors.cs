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

    /// <summary>S-31: no active Location has this QR self-servis check-in code for the tenant.</summary>
    public const string SelfCheckInInvalidCode = "attendance.self_checkin.invalid_code";

    /// <summary>S-31: the GPS check-in named a LocationId that doesn't resolve to an active tenant Location.</summary>
    public const string SelfCheckInLocationNotFound = "attendance.self_checkin.location_not_found";

    /// <summary>S-31: the location has a geofence radius but the request carried no (complete) coordinates.</summary>
    public const string SelfCheckInCoordinatesRequired = "attendance.self_checkin.coordinates_required";

    /// <summary>S-31: the caller's GPS coordinates are farther from the location's geofence center than its configured radius.</summary>
    public const string SelfCheckInOutsideGeofence = "attendance.self_checkin.outside_geofence";
}
