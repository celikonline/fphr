namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// One employee's approved leave day counts inside a period, bucketed for Attendance's "Aylık Puantaj" (S-11):
/// Sick-coded types → <see cref="SickLeaveDays"/> ("Raporlu"), Unpaid/PostnatalUnpaid-coded types →
/// <see cref="UnpaidLeaveDays"/> ("Ücretsiz izin"), every other type (including Annual) → <see cref="PaidLeaveDays"/>
/// ("Ücretli izin"). An employee with no approved leave in the period does not appear in the result.
/// </summary>
public sealed record LeaveDaySummary(Guid EmployeeId, decimal PaidLeaveDays, decimal UnpaidLeaveDays, decimal SickLeaveDays);

/// <summary>
/// Leave's monthly summary read port. Approved requests intersecting the window are allocated to their
/// counted dates, including requests spanning months. Allocations preserve the request's stored total;
/// half-day/hourly requests retain their fractional days and custom types honor their paid setting.
/// </summary>
public interface ILeaveDayReader
{
    Task<IReadOnlyList<LeaveDaySummary>> GetMonthlySummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
