using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Infrastructure.Persistence;

namespace Sense.Modules.Attendance.Infrastructure.Lookups;

/// <summary>Implements Attendance.Contracts.IAttendanceMonthlySummaryReader (Payroll bridge, S-8).</summary>
public sealed class AttendanceMonthlySummaryReader(AttendanceDbContext context, IWorkScheduleReadService schedules, NonWorkingDayResolver nonWorkingDays)
    : IAttendanceMonthlySummaryReader
{
    public async Task<IReadOnlyList<AttendanceMonthlySummary>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds,
        DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        // The optional window is clamped into the month; no window = the whole month.
        var windowStart = from is { } f && f > monthStart ? f : monthStart;
        var windowEnd = to is { } t && t < monthEnd ? t : monthEnd;
        if (windowStart > windowEnd)
        {
            return [];
        }

        var ids = employeeIds.ToList();

        var rows = await context.AttendanceDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.EmployeeId) && d.Date >= windowStart && d.Date <= windowEnd)
            .Select(d => new { d.EmployeeId, d.Date, d.PlannedMinutes, d.WorkedMinutes, d.NetWorkedMinutes, d.OverrideWorkedMinutes,
                d.CreatedAt, d.ModifiedDate, d.Anomalies, d.ResolutionState })
            .ToListAsync(cancellationToken);

        var isPeriodLocked = await context.ShiftPlanLocks.AsNoTracking()
            .AnyAsync(l => l.TenantId == tenantId && l.PeriodMonth == monthStart && l.UnlockedAt == null, cancellationToken);

        var nonWorking = await nonWorkingDays.ResolveAsync(tenantId, ids, windowStart, windowEnd, cancellationToken);
        var approvals = await context.MonthlyTimesheetApprovals.AsNoTracking()
            .Where(a => a.TenantId == tenantId && ids.Contains(a.EmployeeId) && a.PeriodMonth == monthStart)
            .ToDictionaryAsync(a => a.EmployeeId, a => a.ApprovedAt, cancellationToken);
        var summaries = new List<AttendanceMonthlySummary>(ids.Count);
        var byEmployee = rows.ToLookup(r => r.EmployeeId);
        foreach (var employeeId in ids)
        {
            var group = byEmployee[employeeId].ToArray();
            if (group.Length == 0 && !nonWorking.HasLeave(employeeId) && !approvals.ContainsKey(employeeId)) continue;
            var isEstimated = group.Length == 0 && !approvals.ContainsKey(employeeId);
            var effectiveMinutes = group.Select(r => r.OverrideWorkedMinutes ?? r.WorkedMinutes).ToList();
            var workedDays = effectiveMinutes.Count(m => m > 0);
            var workedMinutesTotal = effectiveMinutes.Sum();
            var overtimeMinutes = group.Sum(r => Math.Max((r.OverrideWorkedMinutes ?? r.WorkedMinutes) - r.PlannedMinutes, 0));

            var workingDays = await schedules.GetWorkingDaysAsync(employeeId, windowStart, windowEnd, cancellationToken);
            // Resmi tatil ve onaylı izin günleri çalışılması gerekmeyen gündür: kayıt yoksa eksik sayılmaz (çalışılmışsa zaten workedDays içinde).
            var workedDates = group.Where(r => (r.OverrideWorkedMinutes ?? r.WorkedMinutes) > 0).Select(r => r.Date).ToHashSet();
            var scheduled = workingDays.ToHashSet();
            // Unscheduled holiday/rest-day work must not hide an absence on a scheduled working day.
            var missingDays = workingDays.Where(d => !workedDates.Contains(d) && !nonWorking.IsHoliday(d))
                .Sum(d => 1m - nonWorking.LeaveFraction(employeeId, d));
            var calendarDates = Enumerable.Range(0, windowEnd.DayNumber - windowStart.DayNumber + 1).Select(i => windowStart.AddDays(i));
            var unpaidLeaveDays = calendarDates.Sum(d => nonWorking.LeaveFraction(employeeId, d, paid: false, sick: false));
            var sickLeaveDays = calendarDates.Sum(d => nonWorking.LeaveFraction(employeeId, d, paid: false, sick: true));
            var expectedWorkedDays = workingDays.Count(d => !nonWorking.Contains(employeeId, d));
            if (isEstimated) missingDays = 0m; // No punches or approval: absence is unknown, not an observed missing day.
            var holidayWorked = workedDates.Count(nonWorking.IsHoliday);
            var restDayWorked = workedDates.Count(d => d.DayOfWeek == DayOfWeek.Sunday && !scheduled.Contains(d) && !nonWorking.IsHoliday(d));

            // Whole missing days are deducted separately by Payroll. Only shortfalls on partially worked, ordinary
            // scheduled days reduce the normal paid hours; overtime and holiday/rest-day work are separate earnings.
            var shortfallMinutes = group.Where(r => scheduled.Contains(r.Date) && !nonWorking.Contains(employeeId, r.Date)
                    && (r.OverrideWorkedMinutes ?? r.WorkedMinutes) > 0)
                .Sum(r => Math.Max(r.PlannedMinutes * (1m - nonWorking.LeaveFraction(employeeId, r.Date)) - (r.OverrideWorkedMinutes ?? r.NetWorkedMinutes), 0m));
            var approved = approvals.TryGetValue(employeeId, out var approvedAt)
                && (nonWorking.LatestLeaveChange(employeeId) is not { } leaveChangedAt || leaveChangedAt <= approvedAt)
                && group.All(r => (r.ModifiedDate ?? r.CreatedAt) <= approvedAt
                    && (r.Anomalies == AttendanceAnomaly.None || r.ResolutionState != AttendanceResolutionState.Unresolved));

            summaries.Add(new AttendanceMonthlySummary(
                employeeId, workedDays, Math.Round(workedMinutesTotal / 60m, 2), missingDays, Math.Round(overtimeMinutes / 60m, 2), isPeriodLocked,
                holidayWorked, restDayWorked, isEstimated ? null : shortfallMinutes / 60m, approved, unpaidLeaveDays, sickLeaveDays, isEstimated, expectedWorkedDays));
        }

        return summaries;
    }
}
