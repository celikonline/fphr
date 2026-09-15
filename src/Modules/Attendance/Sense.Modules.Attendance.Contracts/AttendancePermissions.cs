using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// Attendance module permissions (11.3.6 "Roller ve yetkiler"). SystemRoleDefinitions already grants "attendance.*"
/// to Owner/Admin/HR/HRSpecialist/Timekeeper and "*.my.*" (self-service) to every base role, so new attendance.*
/// keys reach the right roles without touching that file.
/// </summary>
public static class AttendancePermissions
{
    private const string Module = "attendance";
    private const string Group = "Attendance";

    public static readonly Permission WorkScheduleView = new(
        "attendance.workschedule.view", Module, "View Work Schedules", Group, "View work schedules and their day tables");

    public static readonly Permission WorkScheduleManage = new(
        "attendance.workschedule.manage", Module, "Manage Work Schedules", Group, "Create, edit, activate/deactivate and set the default work schedule");

    public static readonly Permission WorkScheduleAssign = new(
        "attendance.workschedule.assign", Module, "Assign Work Schedules", Group, "Assign a work schedule to employees");

    /// <summary>"Çalışma tablosu" field on the employee card and Puantajım summary; "*.my.*" reaches every base role.</summary>
    public static readonly Permission MyWorkSchedule = new(
        "attendance.my.workschedule", Module, "View My Work Schedule", Group, "View the caller's own work schedule");

    public static IEnumerable<Permission> All =>
        [WorkScheduleView, WorkScheduleManage, WorkScheduleAssign, MyWorkSchedule];
}
