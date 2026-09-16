namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// One employee's one calendar month, aggregated from <c>AttendanceDay</c> rows (Payroll bridge, S-8, tur 3 PO
/// kararı ile onaylı sözleşme). <see cref="WorkedDays"/>/<see cref="WorkedHours"/> come from recorded (or manually
/// overridden) worked minutes; <see cref="MissingDays"/> is working days per the employee's schedule minus recorded
/// working days (same projection <c>TimesheetWeekEvaluator</c> uses per week, here summed over the month);
/// <see cref="OvertimeHours"/> is the sum of worked-minus-planned minutes on days worked more than planned.
/// <see cref="IsPeriodLocked"/> reflects the month's <c>ShiftPlanLock</c> (Attendance's only period-lock concept).
/// </summary>
public sealed record AttendanceMonthlySummary(
    Guid EmployeeId, decimal WorkedDays, decimal WorkedHours, decimal MissingDays, decimal OvertimeHours, bool IsPeriodLocked);

/// <summary>
/// Cross-module read port (Payroll consumes this instead of guessing worked/missing days from its own data). An
/// employee absent from the result had no AttendanceDay rows at all that month - Payroll distinguishes "veri yok"
/// from "0 gün" by absence from the list, not by a zero row.
/// </summary>
public interface IAttendanceMonthlySummaryReader
{
    Task<IReadOnlyList<AttendanceMonthlySummary>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
