namespace Sense.Modules.Payroll.Application.Ports;

/// <summary>
/// One employee's resolved attendance figures for one payroll period's calendar month (TB-7 "Attendance köprüsü").
/// <see cref="WorkedDays"/> is the only figure <see cref="Domain.Calculation.PayrollCalculator"/> currently consumes
/// (feeds <see cref="Domain.Calculation.PayrollCalculationInput.WorkedDays"/>); <see cref="MissingDays"/> and
/// <see cref="OvertimeHours"/> are informational this slice, reserved for future pro-ration/overtime-pay work
/// (P4+ - see PayrollCalculationInput's own TODO).
/// </summary>
public sealed record WorkedTimeSummary(int WorkedDays, int MissingDays, decimal OvertimeHours, int PublicHolidayDaysWorked = 0, int WeeklyRestDaysWorked = 0);

/// <summary>
/// Payroll's read port for the employee's monthly worked-time figures - deliberately narrow, mirrors Leave's
/// IWorkScheduleReader (Sense.Modules.Leave.Application/Ports.cs) and its WorkScheduleReaderAdapter pattern
/// (adapt an Attendance.Contracts reader in Infrastructure, register in the module's ServiceCollectionExtensions).
/// <c>AttendanceWorkedTimeReaderAdapter</c> (Payroll.Infrastructure/Adapters) adapts
/// Attendance.Contracts.IAttendanceMonthlySummaryReader (S-8, TB-7); it returns null when Attendance has no data for
/// the employee/period, which the calculation engine treats as "keep the full-calendar-month assumption".
/// </summary>
public interface IWorkedTimeReader
{
    /// <summary>Null = no data for this employee/period (Attendance not tracking it yet, or no adapter wired) - the
    /// caller must fall back to the full calendar month, exactly like before this port existed.</summary>
    /// <param name="from">Optional window start inside the month (kıst ayı: işe giriş günü); null = whole month.</param>
    /// <param name="to">Optional window end inside the month (kıst ayı: işten çıkış günü); null = whole month. Days outside the
    /// window are never reported as missing.</param>
    Task<WorkedTimeSummary?> GetAsync(
        Guid tenantId, Guid employeeId, int year, int month,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);
}
