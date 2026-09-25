using Microsoft.EntityFrameworkCore;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Attendance.Infrastructure.Persistence;
using Sense.Modules.Leave.Contracts;

namespace Sense.Modules.Attendance.Infrastructure.Lookups;

/// <summary>
/// Implements Attendance.Contracts.IAttendanceDailyKpiReader (Analytics zaman KPI'ları, zaman-devam Z1.3). Caller passes the
/// already-scoped employee ids; this reader never resolves org structure. "Planlı" = the day carries a schedule snapshot
/// (PlannedMinutes &gt; 0) or a published <c>Work</c> shift assignment (weekly rest / rest day / public holiday rows are not planned days); approved leave (Leave.Contracts, optional port) counts as on-leave.
/// </summary>
public sealed class AttendanceDailyKpiReader(AttendanceDbContext context, ITenantLocalClock localClock, IApprovedLeaveDateLookup? leaveDates = null) : IAttendanceDailyKpiReader
{
    public async Task<IReadOnlyList<DailyAttendanceTotalsDto>> DailyTotalsAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        if (employeeIds.Count == 0 || to < from)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        var days = await context.AttendanceDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to)
            .Select(d => new { d.EmployeeId, d.Date, d.PlannedMinutes, d.CheckIn, d.Anomalies })
            .ToListAsync(ct);
        var shifts = await PublishedShiftsAsync(tenantId, from, to, ids, ct);
        var today = DateOnly.FromDateTime((await localClock.NowAsync(tenantId, ct)).Local);
        var leave = leaveDates is null
            ? []
            : (await leaveDates.GetApprovedLeaveDatesAsync(tenantId, from, to, ids, ct)).ToHashSet();

        var result = new List<DailyAttendanceTotalsDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayRows = days.Where(d => d.Date == date).ToList();
            var arrived = dayRows.Where(d => d.CheckIn is not null).Select(d => d.EmployeeId).ToHashSet();
            var planned = dayRows.Where(d => d.PlannedMinutes > 0).Select(d => d.EmployeeId)
                .Concat(shifts.Where(s => s.Date == date).Select(s => s.EmployeeId))
                .ToHashSet();
            var onLeave = leave.Where(l => l.Date == date).Select(l => l.EmployeeId).ToHashSet();
            // Bugünden sonraki günler devamsızlık sayılmaz (henüz gelinmemiş).
            var absent = date > today ? 0 : planned.Count(e => !arrived.Contains(e) && !onLeave.Contains(e));

            result.Add(new DailyAttendanceTotalsDto(
                date,
                planned.Count,
                arrived.Count,
                dayRows.Count(d => (d.Anomalies & AttendanceAnomaly.LateArrival) != 0),
                dayRows.Count(d => (d.Anomalies & AttendanceAnomaly.EarlyLeave) != 0),
                absent,
                onLeave.Count));
        }

        return result;
    }

    public async Task<IReadOnlyList<EmployeeAttendancePeriodDto>> EmployeeTotalsAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        if (employeeIds.Count == 0 || to < from)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        var days = await context.AttendanceDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.EmployeeId) && d.Date >= from && d.Date <= to)
            .Select(d => new
            {
                d.EmployeeId, d.Date, d.Anomalies, d.CheckIn, d.CheckOut, d.PlannedStart, d.PlannedEnd, d.OvertimeMinutes,
                d.WorkedMinutes, d.OverrideWorkedMinutes,
            })
            .ToListAsync(ct);
        var shifts = await PublishedShiftsAsync(tenantId, from, to, ids, ct);

        var workedDays = days.Where(d => (d.OverrideWorkedMinutes ?? d.WorkedMinutes) > 0).Select(d => (d.EmployeeId, d.Date)).ToHashSet();
        var employees = days.Select(d => d.EmployeeId).Concat(shifts.Select(s => s.EmployeeId)).Distinct();

        var result = new List<EmployeeAttendancePeriodDto>();
        foreach (var employeeId in employees)
        {
            var mine = days.Where(d => d.EmployeeId == employeeId).ToList();
            var late = mine.Where(d => (d.Anomalies & AttendanceAnomaly.LateArrival) != 0 && d.CheckIn is not null && d.PlannedStart is not null).ToList();
            var early = mine.Where(d => (d.Anomalies & AttendanceAnomaly.EarlyLeave) != 0 && d.CheckOut is not null && d.PlannedEnd is not null).ToList();
            var myShifts = shifts.Where(s => s.EmployeeId == employeeId).ToList();

            result.Add(new EmployeeAttendancePeriodDto(
                employeeId,
                mine.Count(d => (d.Anomalies & AttendanceAnomaly.LateArrival) != 0),
                late.Sum(d => MinutesAfter(d.CheckIn!.Value, d.PlannedStart!.Value)),
                mine.Count(d => (d.Anomalies & AttendanceAnomaly.EarlyLeave) != 0),
                early.Sum(d => MinutesAfter(d.PlannedEnd!.Value, d.CheckOut!.Value)),
                mine.Sum(d => d.OvertimeMinutes),
                myShifts.Count,
                myShifts.Count(s => workedDays.Contains((employeeId, s.Date)))));
        }

        return result;
    }

    private async Task<List<(Guid EmployeeId, DateOnly Date)>> PublishedShiftsAsync(
        Guid tenantId, DateOnly from, DateOnly to, List<Guid> ids, CancellationToken ct)
    {
        var rows = await context.ShiftAssignments.AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.EmployeeId) && s.Date >= from && s.Date <= to && s.Status == ShiftAssignmentStatus.Published && s.DayType == ShiftDayType.Work)
            .Select(s => new { s.EmployeeId, s.Date })
            .ToListAsync(ct);
        return rows.Select(r => (r.EmployeeId, r.Date)).ToList();
    }

    /// <summary>
    /// Minutes by which <paramref name="actual"/> is after <paramref name="reference"/> on the wall clock, as a difference
    /// wrapped into (-12h, 12h] so a shift straddling midnight (22:00 start, 00:10 arrival) is 130 min late, not negative;
    /// earlier-than-reference is 0.
    /// </summary>
    public static int MinutesAfter(TimeOnly actual, TimeOnly reference)
    {
        const int day = 24 * 60;
        const int halfDay = day / 2;
        var diff = ((((int)(actual - reference).TotalMinutes) % day) + day) % day;
        return diff > halfDay ? 0 : diff;
    }
}
