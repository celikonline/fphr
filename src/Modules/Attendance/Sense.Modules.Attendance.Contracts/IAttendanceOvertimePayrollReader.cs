namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// One employee's one calendar month of Attendance-side overtime facts (kart senseik-014 §6, "Puantaj → Bordro
/// Kontratı"). Deliberately a NEW, parallel contract next to <see cref="IAttendanceMonthlySummaryReader"/> - that one
/// keeps its existing shape for callers that only need worked/missing days; this one is for Payroll consumers that
/// need the m.63 denkleştirme distinction between a provisional daily figure and a period-closed final one.
/// </summary>
public sealed record AttendanceOvertimePayrollLine(
    Guid EmployeeId,
    /// <summary>Sum of <c>AttendanceDay.OvertimeMinutes</c> for the month (daily proxy, always available).</summary>
    int ProvisionalOvertimeMinutes,
    /// <summary>
    /// Set only when a m.63 denkleştirme period covering days in this month CLOSED with a PeriodEnd inside the month
    /// (<c>EqualizationPeriod.TrueUpOvertimeMinutes</c>) - null means either equalization is off for the tenant, or no
    /// period closed in this month yet, and Payroll should fall back to <see cref="ProvisionalOvertimeMinutes"/>.
    /// </summary>
    int? FinalizedOvertimeMinutes,
    bool EqualizationApplies);

/// <summary>
/// Cross-module read port (Payroll bridge). Implemented by Attendance.Infrastructure, registered by that module. Same
/// "absence = no data" contract as <see cref="IAttendanceMonthlySummaryReader"/>: an employee with zero AttendanceDay
/// rows that month is left out of the result entirely, even when equalization has a finalized figure for them.
/// </summary>
public interface IAttendanceOvertimePayrollReader
{
    Task<IReadOnlyList<AttendanceOvertimePayrollLine>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
