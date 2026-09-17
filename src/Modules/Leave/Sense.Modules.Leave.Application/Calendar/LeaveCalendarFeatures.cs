using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Calendar;

/// <summary>
/// One leave on the calendar. <see cref="IsMasked"/>: sensitive types and types hidden from the calendar show only as "on leave"
/// (type code and name omitted). <see cref="LeaveTypeName"/> is the stored name; the API localizes resource keys.
/// </summary>
public sealed record LeaveCalendarEntryDto(
    Guid RequestId, Guid EmployeeId, string EmployeeName, DateOnly StartDate, DateOnly EndDate, decimal CalculatedDays, LeaveRequestStatus Status,
    bool IsMasked, LeaveTypeCode? LeaveTypeCode, string? LeaveTypeName, int OverlappingApprovedCount = 0);

/// <summary>
/// İzin takvimi: pending and approved leave overlapping [From, To] (at most 93 days). Visible employees: the caller's
/// leave.calendar.read data scope when held; otherwise everyone when Calendar.ShowAllLeaves is on; otherwise only the caller.
/// </summary>
[RequiresPermission("leave.my.calendar")]
[RequiresAccessSetting(AccessSettingKeys.CalendarShowMenu)]
public sealed record GetLeaveCalendarQuery(DateOnly From, DateOnly To, Guid? OrgUnitId) : IQuery<IReadOnlyList<LeaveCalendarEntryDto>>;

public sealed class GetLeaveCalendarHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IDataScopeService dataScope,
    IAccessSettingsService accessSettings,
    IEmployeeDirectory employees,
    IEmployeeLookup employeeLookup,
    ILeaveRequestRepository requests,
    ILeaveRuleSetRepository ruleSets)
    : IQueryHandler<GetLeaveCalendarQuery, IReadOnlyList<LeaveCalendarEntryDto>>
{
    public async Task<Result<IReadOnlyList<LeaveCalendarEntryDto>>> Handle(GetLeaveCalendarQuery query, CancellationToken cancellationToken)
    {
        var range = LeaveCalendar.ValidateRange(query.From, query.To);
        if (range.IsFailure)
        {
            return range.Error;
        }

        IReadOnlyCollection<Guid>? visible = await VisibleEmployeeIdsAsync(cancellationToken);
        if (query.OrgUnitId is { } orgUnitId)
        {
            var inUnit = await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId], cancellationToken);
            visible = visible is null ? inUnit : inUnit.Where(visible.Contains).ToList();
        }

        if (visible is { Count: 0 })
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var leaves = await requests.ListActiveInRangeAsync(query.From, query.To, visible, cancellationToken);
        if (leaves.Count == 0)
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var types = (await ruleSets.ListAsync(cancellationToken)).SelectMany(s => s.Types).ToDictionary(t => t.Id);
        var names = (await employees.GetContactsAsync(tenant.TenantId, leaves.Select(l => l.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var entries = new List<LeaveCalendarEntryDto>();
        foreach (var leave in leaves.OrderBy(l => l.StartDate).ThenBy(l => l.EmployeeName ?? string.Empty))
        {
            var overlappingCount = await GetApprovedOverlapCountAsync(leave.EmployeeId, leave.StartDate, cancellationToken);
            var type = types.GetValueOrDefault(leave.LeaveTypeId);
            var masked = LeaveCalendar.IsMasked(type);
            entries.Add(new LeaveCalendarEntryDto(
                leave.Id, leave.EmployeeId, names.GetValueOrDefault(leave.EmployeeId) ?? string.Empty,
                leave.StartDate, leave.EndDate, leave.CalculatedDays, leave.Status, masked,
                masked ? null : type!.Code, masked ? null : type!.Name,
                overlappingCount));
        }

        return Result.Success(entries);
    }

    private async Task<IReadOnlyCollection<Guid>?> VisibleEmployeeIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } userId && await permissions.HasAsync(userId, LeavePermissions.CalendarRead.Key, cancellationToken))
        {
            return await dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.CalendarRead.Key, cancellationToken);
        }

        if (await accessSettings.IsEnabledForCurrentUserAsync(AccessSettingKeys.CalendarShowAllLeaves, cancellationToken))
        {
            return null;
        }

        return currentUser.EmployeeId is { } employeeId ? [employeeId] : [];
    }

    /// <summary>Count of approved leave requests on the same start date from the same org unit, excluding the caller.</summary>
    private async Task<int> GetApprovedOverlapCountAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken);
        if (orgUnitId is null)
        {
            return 0;
        }

        var peers = (await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId.Value], cancellationToken))
            .Where(id => id != employeeId).ToList();
        if (peers.Count == 0)
        {
            return 0;
        }

        var approved = await requests.ListActiveInRangeAsync(date, date, peers, cancellationToken);
        return approved.Count(r => r.Status == LeaveRequestStatus.Approved);
    }
}

/// <summary>İzin Talepleri: pending leave requests of the employees in the caller's leave.requests.read data scope (Team for a Manager).</summary>
[RequiresPermission("leave.requests.read")]
public sealed record ListTeamLeaveRequestsQuery : IQuery<IReadOnlyList<LeaveCalendarEntryDto>>;

public sealed class ListTeamLeaveRequestsHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IDataScopeService dataScope,
    IEmployeeDirectory employees,
    IEmployeeLookup employeeLookup,
    ILeaveRequestRepository requests,
    ILeaveRuleSetRepository ruleSets,
    TimeProvider clock)
    : IQueryHandler<ListTeamLeaveRequestsQuery, IReadOnlyList<LeaveCalendarEntryDto>>
{
    public async Task<Result<IReadOnlyList<LeaveCalendarEntryDto>>> Handle(ListTeamLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(LeavePermissions.RequestsRead.Key, cancellationToken);
        if (visible is { Count: 0 })
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var candidates = await requests.ListActiveInRangeAsync(today, today.AddDays(LeaveLimits.CalendarMaxRangeDays), visible, cancellationToken);
        var pending = candidates.Where(r => r.Status == LeaveRequestStatus.Pending && r.EmployeeId != currentUser.EmployeeId).ToList();
        if (pending.Count == 0)
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var types = (await ruleSets.ListAsync(cancellationToken)).SelectMany(s => s.Types).ToDictionary(t => t.Id);
        var names = (await employees.GetContactsAsync(tenant.TenantId, pending.Select(r => r.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var entries = new List<LeaveCalendarEntryDto>();
        foreach (var leave in pending.OrderBy(r => r.StartDate).ThenBy(r => r.EmployeeName ?? string.Empty))
        {
            var overlappingCount = await GetApprovedOverlapCountAsync(leave.EmployeeId, leave.StartDate, cancellationToken);
            var type = types.GetValueOrDefault(leave.LeaveTypeId);
            var masked = LeaveCalendar.IsMasked(type);
            entries.Add(new LeaveCalendarEntryDto(
                leave.Id, leave.EmployeeId, names.GetValueOrDefault(leave.EmployeeId) ?? string.Empty,
                leave.StartDate, leave.EndDate, leave.CalculatedDays, leave.Status, masked,
                masked ? null : type!.Code, masked ? null : type!.Name,
                overlappingCount));
        }

        return Result.Success(entries);
    }

    /// <summary>Count of approved leave requests on the same start date from the same org unit, excluding the caller.</summary>
    private async Task<int> GetApprovedOverlapCountAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken);
        if (orgUnitId is null)
        {
            return 0;
        }

        var peers = (await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId.Value], cancellationToken))
            .Where(id => id != employeeId).ToList();
        if (peers.Count == 0)
        {
            return 0;
        }

        var approved = await requests.ListActiveInRangeAsync(date, date, peers, cancellationToken);
        return approved.Count(r => r.Status == LeaveRequestStatus.Approved);
    }
}

public static class LeaveCalendar
{
    public static Result ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        return to.DayNumber - from.DayNumber + 1 > LeaveLimits.CalendarMaxRangeDays
            ? Error.Validation(LeaveErrors.CalendarRangeTooLarge, ("max", LeaveLimits.CalendarMaxRangeDays))
            : Result.Success();
    }

    /// <summary>A type is masked when sensitive, hidden from the calendar, or no longer known (deleted rule set).</summary>
    public static bool IsMasked(LeaveType? type) => type is null || type.IsSensitive || !type.ShowTypeNameOnCalendar;

    public static IReadOnlyList<LeaveCalendarEntryDto> Build(
        IEnumerable<LeaveRequest> leaves, IReadOnlyDictionary<Guid, LeaveType> types, IReadOnlyDictionary<Guid, string> employeeNames) =>
        leaves
            .Select(l =>
            {
                var type = types.GetValueOrDefault(l.LeaveTypeId);
                var masked = IsMasked(type);
                return new LeaveCalendarEntryDto(
                    l.Id, l.EmployeeId, employeeNames.GetValueOrDefault(l.EmployeeId) ?? string.Empty, l.StartDate, l.EndDate, l.CalculatedDays, l.Status, masked,
                    masked ? null : type!.Code, masked ? null : type!.Name);
            })
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
