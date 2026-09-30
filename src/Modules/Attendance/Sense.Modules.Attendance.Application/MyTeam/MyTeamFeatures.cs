using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.MyTeam;

/// <summary>
/// "Ekibim" (FG-007) scope: the caller's direct reports only (Employee.Contracts.IEmployeeLookup.GetDirectReportIdsAsync), never the
/// Team data scope. The caller is always <see cref="ICurrentUser.EmployeeId"/> - no employee id is taken from the request - and a
/// caller without a linked employee or without direct reports resolves to an empty set, so every "my team" query returns [].
/// </summary>
internal static class MyTeamScope
{
    public static async Task<IReadOnlyList<Guid>> DirectReportIdsAsync(
        ITenantContext tenant, ICurrentUser currentUser, IEmployeeLookup employeeLookup, CancellationToken cancellationToken) =>
        currentUser.EmployeeId is { } managerId
            ? await employeeLookup.GetDirectReportIdsAsync(tenant.TenantId, managerId, cancellationToken)
            : [];
}

/// <summary>Ekibim haftalık puantaj (FG-007): same row shape as the data-scope team grid, one calendar week (Monday-based).</summary>
[RequiresPermission(AttendancePermissions.MyTeamKey)]
public sealed record GetMyTeamWeekQuery(DateOnly WeekStart) : IQuery<IReadOnlyList<TeamWeekRowDto>>;

public sealed class GetMyTeamWeekHandler(
    ITenantContext tenant, ICurrentUser currentUser, IEmployeeLookup employeeLookup, IEmployeeDirectory employees, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, NonWorkingDayResolver nonWorkingDays)
    : IQueryHandler<GetMyTeamWeekQuery, IReadOnlyList<TeamWeekRowDto>>
{
    public async Task<Result<IReadOnlyList<TeamWeekRowDto>>> Handle(GetMyTeamWeekQuery query, CancellationToken cancellationToken)
    {
        var employeeIds = await MyTeamScope.DirectReportIdsAsync(tenant, currentUser, employeeLookup, cancellationToken);
        var weekStart = TimesheetWeekEvaluator.MondayOf(query.WeekStart);
        return await TeamRangeBuilder.BuildForEmployeesAsync(
            weekStart, weekStart.AddDays(6), employeeIds, tenant, employees, days, schedules, nonWorkingDays, cancellationToken);
    }
}

/// <summary>Ekibim aylık puantaj (FG-007): same row shape, one row per day of the calendar month containing MonthStart.</summary>
[RequiresPermission(AttendancePermissions.MyTeamKey)]
public sealed record GetMyTeamMonthQuery(DateOnly MonthStart) : IQuery<IReadOnlyList<TeamWeekRowDto>>;

public sealed class GetMyTeamMonthHandler(
    ITenantContext tenant, ICurrentUser currentUser, IEmployeeLookup employeeLookup, IEmployeeDirectory employees, IAttendanceDayRepository days,
    IWorkScheduleReadService schedules, NonWorkingDayResolver nonWorkingDays)
    : IQueryHandler<GetMyTeamMonthQuery, IReadOnlyList<TeamWeekRowDto>>
{
    public async Task<Result<IReadOnlyList<TeamWeekRowDto>>> Handle(GetMyTeamMonthQuery query, CancellationToken cancellationToken)
    {
        var employeeIds = await MyTeamScope.DirectReportIdsAsync(tenant, currentUser, employeeLookup, cancellationToken);
        var monthStart = new DateOnly(query.MonthStart.Year, query.MonthStart.Month, 1);
        return await TeamRangeBuilder.BuildForEmployeesAsync(
            monthStart, monthStart.AddMonths(1).AddDays(-1), employeeIds, tenant, employees, days, schedules, nonWorkingDays, cancellationToken);
    }
}

/// <summary>Today's status of one direct report. Leave type is deliberately not exposed (only <see cref="MyTeamTodayStatus.OnLeave"/>).</summary>
public enum MyTeamTodayStatus
{
    CheckedIn = 0,
    CheckedOut = 1,
    OnLeave = 2,
    NotCheckedIn = 3,
}

public sealed record MyTeamTodayDto(Guid EmployeeId, string EmployeeName, MyTeamTodayStatus Status, TimeOnly? CheckIn, TimeOnly? CheckOut);

public static class MyTeamTodayStatusMapper
{
    /// <summary>
    /// A punch wins over leave (someone on approved leave who still checked in shows as checked in): open punch (also last night's
    /// still-open evening check-in, as on the live board) = CheckedIn, a check-out = CheckedOut, otherwise approved leave today =
    /// OnLeave, otherwise NotCheckedIn.
    /// </summary>
    public static MyTeamTodayStatus Map(LiveAttendanceStatusDto row, bool onLeaveToday) =>
        row.IsInside ? MyTeamTodayStatus.CheckedIn
        : row.CheckOut is not null ? MyTeamTodayStatus.CheckedOut
        : onLeaveToday ? MyTeamTodayStatus.OnLeave
        : MyTeamTodayStatus.NotCheckedIn;
}

/// <summary>
/// Ekibim "bugün" (FG-007): each direct report's status for the tenant-local today, built on the live board's rows
/// (<see cref="LiveAttendanceBoard"/>) plus Leave's approved leave days. No "remote" status - there is no data model for it.
/// </summary>
[RequiresPermission(AttendancePermissions.MyTeamKey)]
public sealed record GetMyTeamTodayQuery : IQuery<IReadOnlyList<MyTeamTodayDto>>;

/// <summary>
/// <paramref name="leaveDates"/> is optional like in <see cref="NonWorkingDayResolver"/>: hosts/fixtures without the Leave module
/// simply report nobody as on leave.
/// </summary>
public sealed class GetMyTeamTodayHandler(
    ITenantContext tenant, ICurrentUser currentUser, IEmployeeLookup employeeLookup, IEmployeeDirectory employees, IAttendanceDayRepository days,
    IPunchRepository punches, ITerminalRepository terminals, ITenantLocalClock clock, IApprovedLeaveDateLookup? leaveDates = null)
    : IQueryHandler<GetMyTeamTodayQuery, IReadOnlyList<MyTeamTodayDto>>
{
    public async Task<Result<IReadOnlyList<MyTeamTodayDto>>> Handle(GetMyTeamTodayQuery query, CancellationToken cancellationToken)
    {
        var employeeIds = await MyTeamScope.DirectReportIdsAsync(tenant, currentUser, employeeLookup, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyTeamTodayDto>>([]);
        }

        var today = DateOnly.FromDateTime((await clock.NowAsync(tenant.TenantId, cancellationToken)).Local);
        var board = await LiveAttendanceBoard.BuildAsync(employeeIds, today, tenant.TenantId, employees, days, punches, terminals, cancellationToken);
        HashSet<Guid> onLeave = leaveDates is null
            ? []
            : (await leaveDates.GetApprovedLeaveDatesAsync(tenant.TenantId, today, today, employeeIds, cancellationToken))
                .Select(l => l.EmployeeId).ToHashSet();

        return board
            .Select(r => new MyTeamTodayDto(
                r.EmployeeId, r.EmployeeName, MyTeamTodayStatusMapper.Map(r, onLeave.Contains(r.EmployeeId)), r.CheckIn, r.CheckOut))
            .OrderBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
