using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>One employee's one day in the team weekly grid (S-8 archive, FE tur 3 sözleşmesi). No record on a working day still reports it as <see cref="AttendanceAnomaly.NoData"/>/Unresolved so the grid can flag it without a stored row.</summary>
public sealed record TeamWeekDayDto(DateOnly Date, Guid? AttendanceDayId, int WorkedMinutes, AttendanceResolutionState Status, IReadOnlyList<AttendanceAnomaly> Anomalies);

public sealed record TeamWeekRowDto(Guid EmployeeId, string EmployeeName, IReadOnlyList<TeamWeekDayDto> Days);

/// <summary>Ekip haftalık puantaj ızgarası (11.3.6): yönetici yalnız attendance.timesheet.view data-scope'unun kapsadığı çalışanları görür, isteğe bağlı birim filtresiyle daraltılabilir.</summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record GetTeamWeekQuery(DateOnly WeekStart, Guid? OrgUnitId) : IQuery<IReadOnlyList<TeamWeekRowDto>>;

public sealed class GetTeamWeekHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees, IAttendanceDayRepository days, IWorkScheduleReadService schedules)
    : IQueryHandler<GetTeamWeekQuery, IReadOnlyList<TeamWeekRowDto>>
{
    public async Task<Result<IReadOnlyList<TeamWeekRowDto>>> Handle(GetTeamWeekQuery query, CancellationToken cancellationToken)
    {
        var weekStart = TimesheetWeekEvaluator.MondayOf(query.WeekStart);
        return await TeamRangeBuilder.BuildAsync(weekStart, weekStart.AddDays(6), query.OrgUnitId, tenant, dataScope, employees, days, schedules, cancellationToken);
    }
}

/// <summary>
/// Ekip aylık ızgara (11.7 yol haritası satır 183: "haftalık puantaj görünümü → aylık ızgaranın ikinci görünümü"):
/// same TeamWeekRowDto shape as <see cref="GetTeamWeekQuery"/>, one row per day of the calendar month instead of
/// one calendar week. Same data-scope/permission as the weekly grid - it is the same screen's other view mode, not
/// a separate one.
/// </summary>
[RequiresPermission("attendance.timesheet.view")]
public sealed record GetTeamMonthQuery(DateOnly MonthStart, Guid? OrgUnitId) : IQuery<IReadOnlyList<TeamWeekRowDto>>;

public sealed class GetTeamMonthHandler(
    ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees, IAttendanceDayRepository days, IWorkScheduleReadService schedules)
    : IQueryHandler<GetTeamMonthQuery, IReadOnlyList<TeamWeekRowDto>>
{
    public async Task<Result<IReadOnlyList<TeamWeekRowDto>>> Handle(GetTeamMonthQuery query, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(query.MonthStart.Year, query.MonthStart.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return await TeamRangeBuilder.BuildAsync(monthStart, monthEnd, query.OrgUnitId, tenant, dataScope, employees, days, schedules, cancellationToken);
    }
}

/// <summary>Shared by the weekly and monthly team grids - same visible-employees resolution and per-day row shape, only the date range differs.</summary>
internal static class TeamRangeBuilder
{
    public static async Task<Result<IReadOnlyList<TeamWeekRowDto>>> BuildAsync(
        DateOnly start, DateOnly end, Guid? orgUnitId, ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees,
        IAttendanceDayRepository days, IWorkScheduleReadService schedules, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, cancellationToken);
        if (orgUnitId is { } unitId)
        {
            var inUnit = await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [unitId], cancellationToken);
            visible = visible is null ? inUnit.ToHashSet() : inUnit.Where(visible.Contains).ToHashSet();
        }

        if (visible is { Count: 0 })
        {
            return Result.Success<IReadOnlyList<TeamWeekRowDto>>([]);
        }

        var employeeIds = visible is null
            ? await employees.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken)
            : visible.ToList();
        if (employeeIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<TeamWeekRowDto>>([]);
        }

        var records = await days.ListForEmployeesAsync(employeeIds, start, end, cancellationToken);
        var recordsByEmployee = records.ToLookup(d => d.EmployeeId);
        var names = (await employees.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var rows = new List<TeamWeekRowDto>(employeeIds.Count);
        foreach (var employeeId in employeeIds)
        {
            var byDate = recordsByEmployee[employeeId].ToDictionary(d => d.Date);
            var dayDtos = new List<TeamWeekDayDto>();
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                if (byDate.TryGetValue(date, out var record))
                {
                    dayDtos.Add(new TeamWeekDayDto(date, record.Id, record.OverrideWorkedMinutes ?? record.WorkedMinutes, record.ResolutionState, SetFlags(record.Anomalies)));
                    continue;
                }

                var isWorkingDay = await schedules.IsWorkingDayAsync(employeeId, date, cancellationToken);
                dayDtos.Add(isWorkingDay
                    ? new TeamWeekDayDto(date, null, 0, AttendanceResolutionState.Unresolved, [AttendanceAnomaly.NoData])
                    : new TeamWeekDayDto(date, null, 0, AttendanceResolutionState.Resolved, []));
            }

            rows.Add(new TeamWeekRowDto(employeeId, names.GetValueOrDefault(employeeId, string.Empty), dayDtos));
        }

        return rows;
    }

    private static IReadOnlyList<AttendanceAnomaly> SetFlags(AttendanceAnomaly anomalies) =>
        Enum.GetValues<AttendanceAnomaly>().Where(f => f != AttendanceAnomaly.None && anomalies.HasFlag(f)).ToList();
}
