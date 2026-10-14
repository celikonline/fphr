namespace Sense.Modules.Payroll.Application.Ports;

/// <summary>
/// Payroll's read port for the employee's monthly unexcused-absence day count (Leave köprüsü, G-7-08/09, faz-07 tur 2
/// madde 3). Deliberately narrow - only <c>AbsenceKind.Unexcused</c> (İK-entered devamsızlık, not PDKS/Attendance
/// data) counts; <c>Excused</c>/<c>Late</c> never reduce <see cref="Domain.Calculation.PayrollCalculationInput.WorkedDays"/>.
/// <c>LeaveAbsenceDaysReaderAdapter</c> (Payroll.Infrastructure/Adapters) adapts Leave.Contracts.IAbsenceReader.
/// </summary>
public interface IAbsenceDaysReader
{
    /// <summary>Null = no data for this employee/period (Leave has no AbsenceRecord rows for the month, or no adapter
    /// wired) - the caller must not treat this as "0 devamsızlık", it means "we don't know", exactly like
    /// <see cref="IWorkedTimeReader"/>'s own null contract.</summary>
    Task<int?> GetUnexcusedDaysAsync(Guid tenantId, Guid employeeId, int year, int month, CancellationToken cancellationToken = default);

    Task<int?> GetUnexcusedDaysAsync(Guid tenantId, Guid employeeId, int year, int month,
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
}
