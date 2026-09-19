using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Leave.Contracts.IApprovedLeaveDateLookup (senseik-029): every date of an Approved leave.leave_requests row that
/// falls in [from, to], one row per employee+date. Mirrors LeaveDayReader's direct-EF-query style; expands each
/// request's span in memory rather than in SQL since the day count per request is small (a few weeks at most).
/// </summary>
public sealed class ApprovedLeaveDateLookup(LeaveDbContext context) : IApprovedLeaveDateLookup
{
    public async Task<IReadOnlyList<ApprovedLeaveDay>> GetApprovedLeaveDatesAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        var requests = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= to && r.EndDate >= from)
            .Select(r => new { r.EmployeeId, r.StartDate, r.EndDate })
            .ToListAsync(cancellationToken);

        var result = new List<ApprovedLeaveDay>();
        foreach (var request in requests)
        {
            var start = request.StartDate < from ? from : request.StartDate;
            var end = request.EndDate > to ? to : request.EndDate;
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                result.Add(new ApprovedLeaveDay(request.EmployeeId, date));
            }
        }

        return result;
    }
}
