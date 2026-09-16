namespace Sense.Modules.Attendance.Application;

/// <summary>Application-layer error codes (cross-module lookups; domain validation lives in AttendanceDomainErrors).</summary>
public static class AttendanceErrors
{
    public const string EmployeeNotFound = "attendance.employee.not_found";
    public const string EmployeeNotInScope = "attendance.employee.not_in_scope";
    public const string AvailabilityDateInPast = "attendance.availability.date_in_past";
}
