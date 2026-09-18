namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// One employee's approved leave day counts inside a period, bucketed for Attendance's "Aylık Puantaj" (S-11):
/// Sick-coded types → <see cref="SickLeaveDays"/> ("Raporlu"), Unpaid/PostnatalUnpaid-coded types →
/// <see cref="UnpaidLeaveDays"/> ("Ücretsiz izin"), every other type (including Annual) → <see cref="PaidLeaveDays"/>
/// ("Ücretli izin"). An employee with no approved leave in the period does not appear in the result.
/// </summary>
public sealed record LeaveDaySummary(Guid EmployeeId, decimal PaidLeaveDays, decimal UnpaidLeaveDays, decimal SickLeaveDays);

/// <summary>
/// Leave's read port for Attendance's monthly puantaj summary (Faz 2 A5, mirrors Attendance.Contracts.IAttendanceMonthlySummaryReader's
/// role for Payroll). Only Approved requests fully contained in [from, to] are counted - a request crossing the
/// period boundary is not day-split (same documented simplification as ILeaveUsageReader.GetAnnualUsageAsync for
/// year-spanning requests); this under-counts a handful of boundary-crossing requests rather than double-counting
/// or guessing a per-day split.
/// </summary>
public interface ILeaveDayReader
{
    Task<IReadOnlyList<LeaveDaySummary>> GetMonthlySummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
