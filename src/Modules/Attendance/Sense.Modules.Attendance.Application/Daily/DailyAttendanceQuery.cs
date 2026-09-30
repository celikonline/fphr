using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Attendance.Application.Daily;

/// <summary>
/// IDailyAttendanceQuery (spec 5.4): resolves the caller's visible employees (attendance.daily.view data scope, optionally
/// narrowed to an org unit and its subtree), classifies each employee's day with <see cref="DailyStatusFilter"/> and pages the
/// requested bucket. Counts use the same classification, so tab badges always equal the list totals.
/// </summary>
public sealed class DailyAttendanceQuery(
    IDataScopeService dataScope, IEmployeeDirectory employees, IOrgUnitLookup orgUnits, IAttendanceDayRepository days, IPunchRepository punches,
    ILeaveOnDateReader leave, IPlannedDayResolver planner) : IDailyAttendanceQuery
{
    private const int MaxPageSize = 200;

    public async Task<PagedResult<DailyAttendanceRowDto>> ListAsync(
        Guid tenantId, DateOnly date, DailyStatus status, Guid? orgUnitId, bool includeSubUnits, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var day = await LoadAsync(tenantId, date, orgUnitId, includeSubUnits, ct);
        var matching = day.Employees.Where(e => e.Statuses.Contains(status)).ToList();
        if (matching.Count == 0)
        {
            return new PagedResult<DailyAttendanceRowDto>([], page, pageSize, 0);
        }

        var names = (await employees.GetContactsAsync(tenantId, matching.Select(e => e.EmployeeId).ToList(), ct))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());
        var ordered = matching
            .OrderBy(e => names.GetValueOrDefault(e.EmployeeId, string.Empty), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.EmployeeId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var pagePunches = (await punches.ListForRangeAsync(tenantId, date, date, ordered.Select(e => e.EmployeeId).ToList(), ct))
            .ToLookup(p => p.EmployeeId);
        var rows = ordered.Select(e => ToRow(e, date, names.GetValueOrDefault(e.EmployeeId, string.Empty), pagePunches[e.EmployeeId])).ToList();
        rows = await WithIncalculableReasonsAsync(rows, ordered, pagePunches, date, ct);
        return new PagedResult<DailyAttendanceRowDto>(rows, page, pageSize, matching.Count);
    }

    public async Task<IReadOnlyDictionary<DailyStatus, int>> CountsAsync(
        Guid tenantId, DateOnly date, Guid? orgUnitId, bool includeSubUnits, CancellationToken ct)
    {
        var day = await LoadAsync(tenantId, date, orgUnitId, includeSubUnits, ct);
        var counts = Enum.GetValues<DailyStatus>().ToDictionary(s => s, _ => 0);
        foreach (var status in day.Employees.SelectMany(e => e.Statuses))
        {
            counts[status]++;
        }

        return counts;
    }

    /// <summary>
    /// Fills <see cref="DailyAttendanceRowDto.IncalculableReason"/> for incalculable days only. The page's punches are already loaded in
    /// one query (no per-row punch read); the plan is resolved just for the (few) incalculable rows, never for every row.
    /// </summary>
    private async Task<List<DailyAttendanceRowDto>> WithIncalculableReasonsAsync(
        List<DailyAttendanceRowDto> rows, IReadOnlyList<EmployeeDay> employeeDays, ILookup<Guid, Domain.Punches.Punch> pagePunches,
        DateOnly date, CancellationToken ct)
    {
        var result = new List<DailyAttendanceRowDto>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var record = employeeDays[i].Record;
            if (record is null || (record.Anomalies & AttendanceAnomaly.Incalculable) == 0)
            {
                result.Add(rows[i]);
                continue;
            }

            var plan = await planner.ResolveAsync(record.EmployeeId, date, record.CheckIn, ct);
            var reason = IncalculableExplainer.Explain(pagePunches[record.EmployeeId].ToList(), plan);
            result.Add(rows[i] with { IncalculableReason = IncalculableExplainer.Code(reason) });
        }

        return result;
    }

    private static DailyAttendanceRowDto ToRow(EmployeeDay e, DateOnly date, string name, IEnumerable<Domain.Punches.Punch> punchesOfEmployee)
    {
        var dayPunches = punchesOfEmployee.ToList();
        var checkInPunch = dayPunches.Where(p => p.Direction == PunchDirection.In).OrderBy(p => p.OccurredAtUtc).FirstOrDefault();
        var checkOutPunch = dayPunches.Where(p => p.Direction == PunchDirection.Out).OrderByDescending(p => p.OccurredAtUtc).FirstOrDefault();
        var record = e.Record;
        return new DailyAttendanceRowDto(
            e.EmployeeId, name, null, date, record?.CheckIn, record?.CheckOut, record?.WorkedMinutes ?? 0, record?.OvertimeMinutes ?? 0,
            AnomalyNames(record?.Anomalies ?? AttendanceAnomaly.None), e.LeaveType, checkInPunch?.Id, checkOutPunch?.Id);
    }

    private static string[] AnomalyNames(AttendanceAnomaly anomalies) =>
        Enum.GetValues<AttendanceAnomaly>()
            .Where(a => a != AttendanceAnomaly.None && anomalies.HasFlag(a))
            .Select(a => char.ToLowerInvariant(a.ToString()[0]) + a.ToString()[1..])
            .ToArray();

    private async Task<DayView> LoadAsync(Guid tenantId, DateOnly date, Guid? orgUnitId, bool includeSubUnits, CancellationToken ct)
    {
        var ids = await ResolveEmployeesAsync(tenantId, orgUnitId, includeSubUnits, ct);
        if (ids.Count == 0)
        {
            return new DayView([]);
        }

        var records = (await days.ListForEmployeesAsync(ids, date, date, ct)).GroupBy(d => d.EmployeeId).ToDictionary(g => g.Key, g => g.First());
        var leaves = (await leave.ListAsync(tenantId, date, ids, ct)).GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => g.First());
        var result = ids.Select(id =>
        {
            records.TryGetValue(id, out var record);
            leaves.TryGetValue(id, out var onLeave);
            return new EmployeeDay(id, record, onLeave?.LeaveTypeCode, DailyStatusFilter.Classify(record, onLeave is not null));
        }).ToList();
        return new DayView(result);
    }

    private async Task<IReadOnlyList<Guid>> ResolveEmployeesAsync(Guid tenantId, Guid? orgUnitId, bool includeSubUnits, CancellationToken ct)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyViewKey, ct);
        if (visible is { Count: 0 })
        {
            return [];
        }

        if (orgUnitId is { } unitId)
        {
            IReadOnlyCollection<Guid> units = includeSubUnits ? await orgUnits.GetSelfAndDescendantIdsAsync(tenantId, [unitId], ct) : [unitId];
            var inUnits = await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, units, ct);
            return visible is null ? inUnits : inUnits.Where(visible.Contains).ToList();
        }

        return visible is null ? await employees.ListActiveEmployeeIdsAsync(tenantId, ct) : visible.ToList();
    }

    private sealed record EmployeeDay(Guid EmployeeId, AttendanceDay? Record, string? LeaveType, IReadOnlySet<DailyStatus> Statuses);

    private sealed record DayView(IReadOnlyList<EmployeeDay> Employees);
}
