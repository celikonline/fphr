using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Calendar;

/// <summary>
/// "Ekibim" izinleri (FG-007): pending and approved leave of the caller's direct reports (Employee.Contracts.IEmployeeLookup
/// .GetDirectReportIdsAsync, never the Team data scope) overlapping [From, To] (at most 93 days, like the calendar), optionally
/// narrowed to one <see cref="Status"/>. Sensitive/hidden types are masked exactly like the calendar (<see cref="LeaveCalendar.Build"/>).
/// The caller is always <see cref="ICurrentUser.EmployeeId"/>; a caller who manages nobody gets [].
/// </summary>
[RequiresPermission("leave.my.team")]
public sealed record ListMyTeamLeaveRequestsQuery(DateOnly From, DateOnly To, LeaveRequestStatus? Status) : IQuery<IReadOnlyList<LeaveCalendarEntryDto>>;

public sealed class ListMyTeamLeaveRequestsHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IEmployeeLookup employeeLookup,
    IEmployeeDirectory employees,
    ILeaveRequestRepository requests,
    ILeaveRuleSetRepository ruleSets)
    : IQueryHandler<ListMyTeamLeaveRequestsQuery, IReadOnlyList<LeaveCalendarEntryDto>>
{
    public async Task<Result<IReadOnlyList<LeaveCalendarEntryDto>>> Handle(ListMyTeamLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        var range = LeaveCalendar.ValidateRange(query.From, query.To);
        if (range.IsFailure)
        {
            return range.Error;
        }

        if (currentUser.EmployeeId is not { } managerId)
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var reports = await employeeLookup.GetDirectReportIdsAsync(tenant.TenantId, managerId, cancellationToken);
        if (reports.Count == 0)
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var leaves = (await requests.ListActiveInRangeAsync(query.From, query.To, reports, cancellationToken))
            .Where(l => query.Status is not { } status || l.Status == status)
            .ToList();
        if (leaves.Count == 0)
        {
            return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>([]);
        }

        var types = (await ruleSets.ListAsync(cancellationToken)).SelectMany(s => s.Types).ToDictionary(t => t.Id);
        var names = (await employees.GetContactsAsync(tenant.TenantId, leaves.Select(l => l.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var entries = new List<LeaveCalendarEntryDto>();
        foreach (var entry in LeaveCalendar.Build(leaves, types, names))
        {
            var overlapping = await LeaveCalendar.ApprovedOverlapCountAsync(
                tenant.TenantId, entry.EmployeeId, entry.StartDate, employeeLookup, employees, requests, cancellationToken);
            entries.Add(entry with { OverlappingApprovedCount = overlapping });
        }

        return Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>(entries);
    }
}
