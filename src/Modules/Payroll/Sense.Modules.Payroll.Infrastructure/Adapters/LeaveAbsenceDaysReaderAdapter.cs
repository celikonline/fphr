using Sense.Modules.Leave.Contracts;
using Sense.Modules.Payroll.Application.Ports;

namespace Sense.Modules.Payroll.Infrastructure.Adapters;

/// <summary>
/// Adapts Leave.Contracts.IAbsenceReader ("the bridge is Faz 7's job", faz-05 tur 3) to Payroll's IAbsenceDaysReader
/// port (G-7-08/09, faz-07 tur 2 madde 3). Mirrors AttendanceWorkedTimeReaderAdapter/ExpensePayoutReaderAdapter's
/// role: adapt one cross-module Contracts reader to this module's own narrow port.
/// </summary>
public sealed class LeaveAbsenceDaysReaderAdapter(IAbsenceReader absences, IApprovedLeaveDateLookup? approvedLeave = null) : IAbsenceDaysReader
{
    public Task<int?> GetUnexcusedDaysAsync(Guid tenantId, Guid employeeId, int year, int month, CancellationToken cancellationToken = default) =>
        GetUnexcusedDaysAsync(tenantId, employeeId, year, month, null, null, cancellationToken);

    public async Task<int?> GetUnexcusedDaysAsync(Guid tenantId, Guid employeeId, int year, int month, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var windowStart = from is { } start && start > monthStart ? start : monthStart;
        var windowEnd = to is { } end && end < monthEnd ? end : monthEnd;
        if (windowEnd < windowStart) return null;

        var records = await absences.ListAsync(tenantId, employeeId, windowStart, windowEnd, cancellationToken);
        if (records.Count == 0)
        {
            // No AbsenceRecord rows at all for this employee/month - "veri yok", not "0 devamsızlık" (Leave has no
            // separate signal for the two - see IAbsenceDaysReader's own doc), so this defers exactly like
            // IWorkedTimeReader's null: the caller keeps its current fallback.
            return null;
        }

        // Only Unexcused counts as a missing day (G-5-09/11: Excused absences on an approved full-day leave are
        // already excluded by Leave's AbsenceReader; Late is hours-only, never a missing day). Distinct dates - a
        // duplicate same-day record (shouldn't happen post-reversal-filter, but defensive) must not double-count.
        HashSet<DateOnly> fullLeaveDates = approvedLeave is null ? [] : (await approvedLeave.GetApprovedLeaveDatesAsync(
            tenantId, windowStart, windowEnd, [employeeId], cancellationToken))
            .GroupBy(d => d.Date).Where(g => g.Sum(d => d.DayFraction) >= 1m).Select(g => g.Key).ToHashSet();
        return records.Where(r => r.Kind == "Unexcused" && !fullLeaveDates.Contains(r.Date)).Select(r => r.Date).Distinct().Count();
    }
}
