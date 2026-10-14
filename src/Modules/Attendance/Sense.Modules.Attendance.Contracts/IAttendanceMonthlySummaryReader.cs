namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// One employee's one calendar month, aggregated from <c>AttendanceDay</c> rows (Payroll bridge, S-8, tur 3 PO
/// kararı ile onaylı sözleşme). <see cref="WorkedDays"/>/<see cref="WorkedHours"/> come from recorded (or manually
/// overridden) worked minutes; <see cref="MissingDays"/> is working days per the employee's schedule minus recorded
/// working days (same projection <c>TimesheetWeekEvaluator</c> uses per week, here summed over the month);
/// <see cref="OvertimeHours"/> is the sum of worked-minus-planned minutes on days worked more than planned.
/// <see cref="PublicHolidayDaysWorked"/> counts official-holiday dates with recorded work and <see cref="WeeklyRestDaysWorked"/> Sundays worked that are not
/// scheduled working days (4857 md.46/47: each earns one extra day's pay); <see cref="IsPeriodLocked"/> reflects the month's <c>ShiftPlanLock</c> (Attendance's only period-lock concept).
/// NormalHoursShortfall uses net working minutes (breaks excluded), or an explicit manual override, on partially worked
/// ordinary scheduled days. It excludes whole-day absences, overtime, approved leave and holiday/rest-day earnings.
/// IsTimesheetApproved requires the employee's monthly approval, resolved anomalies and no AttendanceDay changes after approval;
/// a shift-plan lock alone is not an approval.
/// </summary>
public sealed record AttendanceMonthlySummary(
    Guid EmployeeId, decimal WorkedDays, decimal WorkedHours, decimal MissingDays, decimal OvertimeHours, bool IsPeriodLocked,
    int PublicHolidayDaysWorked = 0, int WeeklyRestDaysWorked = 0,
    decimal? NormalHoursShortfall = null, bool IsTimesheetApproved = false, decimal UnpaidLeaveDays = 0m, decimal SickLeaveDays = 0m, bool IsEstimated = false, int ExpectedWorkedDays = 0);

/// <summary>
/// Cross-module read port (Payroll consumes this instead of guessing worked/missing days from its own data). An
/// employee absent from the result had no AttendanceDay rows at all that month - Payroll distinguishes "veri yok"
/// from "0 gün" by absence from the list, not by a zero row.
/// </summary>
public interface IAttendanceMonthlySummaryReader
{
    /// <param name="from">Optional window start (clamped into the month); null = first day of the month.</param>
    /// <param name="to">Optional window end (clamped into the month); null = last day of the month. A window narrower than the
    /// month (Payroll's kıst ayı: işe giriş/çıkış arası) limits the rows, the scheduled working days and the holiday/rest-day counts to
    /// that window, so days outside the employment are never counted as missing. Null/null = the whole month, the pre-existing behaviour.</param>
    Task<IReadOnlyList<AttendanceMonthlySummary>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);
}
