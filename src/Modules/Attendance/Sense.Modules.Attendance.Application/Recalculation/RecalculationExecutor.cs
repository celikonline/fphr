using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Employee.Contracts;

namespace Sense.Modules.Attendance.Application.Recalculation;

/// <summary>
/// K6 recalculation core (zaman-devam Z0.3): rebuilds days of [from, to] employee-chunk by employee-chunk (200 per
/// transaction). Days in a month the employee's timesheet was approved for are skipped; a human resolution
/// (Resolved/Ignored + override) given before the run survives it; the run is idempotent (running twice yields the same
/// rows). Tenant context must already be set by the caller (background scope).
/// </summary>
public sealed class RecalculationExecutor(
    IAttendanceDayRepository days, IMonthlyTimesheetApprovalRepository approvals, IDayRebuilder rebuilder,
    IEmployeeDirectory employees, IAttendanceUnitOfWork unitOfWork)
{
    public const int ChunkSize = 200;

    /// <returns>Number of days rebuilt.</returns>
    public async Task<int> RunAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds,
        Action<int, int>? progress, CancellationToken cancellationToken)
    {
        var ids = employeeIds is { Count: > 0 }
            ? employeeIds.Distinct().ToList()
            : (await employees.ListActiveEmployeeIdsAsync(tenantId, cancellationToken).ConfigureAwait(false)).ToList();
        var dates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
        var total = ids.Count * dates.Count;
        var months = dates.Select(MonthlyTimesheetApproval.NormalizeToMonthStart).Distinct().ToList();

        var done = 0;
        var rebuilt = 0;
        progress?.Invoke(total, done);
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            rebuilt += await RunChunkAsync(tenantId, chunk, dates, months, cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            done += chunk.Length * dates.Count;
            progress?.Invoke(total, done);
        }

        return rebuilt;
    }

    private async Task<int> RunChunkAsync(
        Guid tenantId, Guid[] chunk, List<DateOnly> dates, List<DateOnly> months, CancellationToken cancellationToken)
    {
        var existing = (await days.ListForEmployeesAsync(chunk, dates[0], dates[^1], cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => (d.EmployeeId, d.Date));

        var approved = new HashSet<(Guid, DateOnly)>();
        foreach (var month in months)
        {
            foreach (var approval in await approvals.ListForPeriodAsync(chunk, month, cancellationToken).ConfigureAwait(false))
            {
                approved.Add((approval.EmployeeId, approval.PeriodMonth));
            }
        }

        var rebuilt = 0;
        foreach (var employeeId in chunk)
        {
            foreach (var date in dates)
            {
                if (approved.Contains((employeeId, MonthlyTimesheetApproval.NormalizeToMonthStart(date))))
                {
                    continue;
                }

                existing.TryGetValue((employeeId, date), out var before);
                var (state, overrideMinutes, reason) = before is null
                    ? (AttendanceResolutionState.Unresolved, (int?)null, (string?)null)
                    : (before.ResolutionState, before.OverrideWorkedMinutes, before.OverrideReason);

                var day = await rebuilder.RebuildAsync(tenantId, employeeId, date, cancellationToken).ConfigureAwait(false);
                if (day is null)
                {
                    continue;
                }

                day.RestoreResolution(state, overrideMinutes, reason);
                rebuilt++;
            }
        }

        return rebuilt;
    }
}
