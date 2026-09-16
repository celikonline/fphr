using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Infrastructure.Persistence;

namespace Sense.Modules.Attendance.Infrastructure.Lookups;

/// <summary>Implements Attendance.Contracts.IAttendanceMonthlySummaryReader (Payroll bridge, S-8).</summary>
public sealed class AttendanceMonthlySummaryReader(AttendanceDbContext context, IWorkScheduleReadService schedules) : IAttendanceMonthlySummaryReader
{
    public async Task<IReadOnlyList<AttendanceMonthlySummary>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var ids = employeeIds.ToList();

        var rows = await context.AttendanceDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to)
            .Select(d => new { d.EmployeeId, d.Date, d.PlannedMinutes, d.WorkedMinutes, d.OverrideWorkedMinutes })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var isPeriodLocked = await context.ShiftPlanLocks.AsNoTracking()
            .AnyAsync(l => l.TenantId == tenantId && l.PeriodMonth == from && l.UnlockedAt == null, cancellationToken);

        var summaries = new List<AttendanceMonthlySummary>(ids.Count);
        foreach (var group in rows.GroupBy(r => r.EmployeeId))
        {
            var effectiveMinutes = group.Select(r => r.OverrideWorkedMinutes ?? r.WorkedMinutes).ToList();
            var workedDays = effectiveMinutes.Count(m => m > 0);
            var workedMinutesTotal = effectiveMinutes.Sum();
            var overtimeMinutes = group.Sum(r => Math.Max((r.OverrideWorkedMinutes ?? r.WorkedMinutes) - r.PlannedMinutes, 0));

            var workingDays = await schedules.GetWorkingDaysAsync(group.Key, from, to, cancellationToken);
            var missingDays = Math.Max(workingDays.Count - workedDays, 0);

            summaries.Add(new AttendanceMonthlySummary(
                group.Key, workedDays, Math.Round(workedMinutesTotal / 60m, 2), missingDays, Math.Round(overtimeMinutes / 60m, 2), isPeriodLocked));
        }

        return summaries;
    }
}
