namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// One employee's one calendar month of overtime pay-rate facts (kart senseik-012 §4, "Puantaj → Bordro Kontratı" -
/// Attendance side is <c>Attendance.Contracts.IAttendanceOvertimePayrollReader</c>). <see cref="Rate25Minutes"/>/
/// <see cref="Rate50Minutes"/> are approved requests whose <c>WorkDate</c> falls in the month and that were NOT
/// converted to time off (still payable in cash - either never converted, or converted with Kind=Pay).
/// <see cref="TimeOffConvertedMinutes"/> is the sum of posted TimeOff conversions' ResultMinutes for approved
/// requests worked in the month - Payroll excludes that portion from cash pay since it was already granted as leave.
/// </summary>
public sealed record OvertimePayrollLine(Guid EmployeeId, int Rate25Minutes, int Rate50Minutes, int TimeOffConvertedMinutes);

/// <summary>Cross-module read port (Payroll bridge). Implemented by Leave.Infrastructure, registered by that module.</summary>
public interface IOvertimePayrollReader
{
    Task<IReadOnlyList<OvertimePayrollLine>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
