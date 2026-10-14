namespace Sense.Modules.Payroll.Application.Ports;

/// <summary>
/// One employee's resolved attendance figures for one payroll period's calendar month (TB-7 "Attendance köprüsü").
/// Missing days affect period wages. Hourly wages also require a current monthly timesheet approval and the normal-hours
/// shortfall on partially worked scheduled days. Observed overtime remains informational; approved cash overtime is read from Leave.
/// </summary>
public sealed record WorkedTimeSummary(
    int WorkedDays, decimal MissingDays, decimal OvertimeHours, int PublicHolidayDaysWorked = 0, int WeeklyRestDaysWorked = 0,
    decimal? NormalHoursShortfall = null, bool IsTimesheetApproved = false, decimal? WorkedHours = null, decimal UnpaidLeaveDays = 0m, decimal SickLeaveDays = 0m, bool IsEstimated = false, int ExpectedWorkedDays = 0);

/// <summary>
/// Payroll's read port for the employee's monthly worked-time figures - deliberately narrow, mirrors Leave's
/// IWorkScheduleReader (Sense.Modules.Leave.Application/Ports.cs) and its WorkScheduleReaderAdapter pattern
/// (adapt an Attendance.Contracts reader in Infrastructure, register in the module's ServiceCollectionExtensions).
/// <c>AttendanceWorkedTimeReaderAdapter</c> (Payroll.Infrastructure/Adapters) adapts
/// Attendance.Contracts.IAttendanceMonthlySummaryReader (S-8, TB-7); it returns null when Attendance has no data for
/// the employee/period. Monthly/daily wages keep their existing fallback; hourly wages report missing attendance explicitly.
/// </summary>
public interface IWorkedTimeReader
{
    /// <summary>Null = no data for this employee/period (Attendance not tracking it yet, or no adapter wired) - the
    /// hourly wages cannot be calculated from a guessed full month.</summary>
    /// <param name="from">Optional window start inside the month (kıst ayı: işe giriş günü); null = whole month.</param>
    /// <param name="to">Optional window end inside the month (kıst ayı: işten çıkış günü); null = whole month. Days outside the
    /// window are never reported as missing.</param>
    Task<WorkedTimeSummary?> GetAsync(
        Guid tenantId, Guid employeeId, int year, int month,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);
}
