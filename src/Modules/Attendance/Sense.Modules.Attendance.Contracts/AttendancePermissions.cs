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

    public static readonly Permission TimesheetView = new(
        "attendance.timesheet.view", Module, "View Timesheets", Group, "View employees' recorded attendance days");

    /// <summary>"Çalışılan saat" manual record/correction (11.3.6); PDKS import (package A2b) reuses the same write path.</summary>
    public static readonly Permission TimesheetEdit = new(
        "attendance.timesheet.edit", Module, "Edit Timesheets", Group, "Record and correct employees' check-in/check-out times");

    /// <summary>"Puantajım"; "*.my.*" reaches every base role.</summary>
    public static readonly Permission MyTimesheet = new(
        "attendance.my.timesheet", Module, "View My Timesheet", Group, "View the caller's own recorded attendance days");

    public const string TimesheetImportKey = "attendance.timesheet.import";

    /// <summary>PDKS Excel import (package A2b); checked on top of datatransfer.import.run by the DataTransfer module's ImportTemplateCatalog.</summary>
    public static readonly Permission TimesheetImport = new(
        TimesheetImportKey, Module, "Import Timesheets (PDKS)", Group, "Import check-in/check-out times from a PDKS Excel file");

    /// <summary>
    /// "Öneri" resolution (package A3, 11.3.6 "Roller ve yetkiler" - spec text names it attendance.timesheet.resolve):
    /// apply Fazla mesai ekle / Çalışılan saat / İzin talebi / Ignore to a flagged day. Reaching Leave for the
    /// overtime/leave-request actions additionally needs the caller's own Leave permissions (leave.requests.create_on_behalf,
    /// leave.requests.bypass_approval, leave.overtimes.create_on_behalf) - this key alone only unlocks Çalışılan saat/Ignore.
    /// SystemRoleDefinitions.cs's Manager entry already anticipated an "attendance.corrections.approve" key for this same
    /// purpose that was never wired up; it is left untouched and this key is appended to Manager's include list instead,
    /// to match the literal permission name the 11.3.06 spec uses for every other attendance.timesheet.* key.
    /// </summary>
    public const string TimesheetResolveKey = "attendance.timesheet.resolve";

    public static readonly Permission TimesheetResolve = new(
        TimesheetResolveKey, Module, "Resolve Timesheet Anomalies", Group, "Apply a suggested resolution (overtime, worked hours, leave request, ignore) to a flagged attendance day");

    /// <summary>
    /// Vardiya izinleri (package A4, 11.3.6 "Roller ve yetkiler"). Timekeeper's "attendance.*" wildcard already
    /// grants view/plan/publish; its exclude list is appended with lock/unlock/settings (11.3.06 role mapping:
    /// "Timekeeper → ... plan/publish ... approve/lock yok") since the placeholder excludes someone pre-wrote there
    /// (attendance.timesheets.lock/.unlock, attendance.settings.*) don't match this module's actual key shape.
    /// Manager's include list is appended with shift.plan/.publish ("Manager → view/resolve/plan/publish @Team").
    /// </summary>
    public static readonly Permission ShiftView = new(
        "attendance.shift.view", Module, "View Shift Plan", Group, "View the shift plan (weekly/monthly views, reports)");

    public static readonly Permission ShiftPlan = new(
        "attendance.shift.plan", Module, "Plan Shifts", Group, "Create, edit and repeat draft shift assignments");

    public static readonly Permission ShiftPublish = new(
        "attendance.shift.publish", Module, "Publish Shifts", Group, "Publish draft shift assignments to employees");

    public static readonly Permission ShiftSettingsManage = new(
        "attendance.shift.settings", Module, "Manage Shift Settings", Group, "Configure daily/weekly limits, night work, break and auto-lock settings");

    public static readonly Permission ShiftLock = new(
        "attendance.shift.lock", Module, "Lock Shift Plan", Group, "Lock a month's shift plan against further changes");

    public static readonly Permission ShiftUnlock = new(
        "attendance.shift.unlock", Module, "Unlock Shift Plan", Group, "Reopen a locked month's shift plan (audited exception)");

    /// <summary>"Vardiyam"; "*.self.*" reaches every base role.</summary>
    public static readonly Permission ShiftSelf = new(
        "attendance.shift.self", Module, "View My Shifts", Group, "View the caller's own shift assignments");

    /// <summary>Package A5. "*.self.*" reaches every base role for self-service submit/cancel.</summary>
    public static readonly Permission AvailabilitySelf = new(
        "attendance.availability.self", Module, "Report My Availability", Group, "Submit and cancel the caller's own \"müsait değilim\" entries");

    public static readonly Permission AvailabilityView = new(
        "attendance.availability.view", Module, "View Employee Availability", Group, "View employees' availability entries when planning shifts");

    public static readonly Permission CompensatoryView = new(
        "attendance.compensatory.view", Module, "View Compensatory Work Ledger", Group, "View employees' telafi çalışması debt/compensation ledger");

    public static readonly Permission CompensatoryManage = new(
        "attendance.compensatory.manage", Module, "Record Compensatory Work", Group, "Log compensatory hours worked against an open telafi debt");

    public static IEnumerable<Permission> All =>
        [
            WorkScheduleView, WorkScheduleManage, WorkScheduleAssign, MyWorkSchedule, TimesheetView, TimesheetEdit, MyTimesheet, TimesheetImport, TimesheetResolve,
            ShiftView, ShiftPlan, ShiftPublish, ShiftSettingsManage, ShiftLock, ShiftUnlock, ShiftSelf,
            AvailabilitySelf, AvailabilityView, CompensatoryView, CompensatoryManage,
        ];
}
