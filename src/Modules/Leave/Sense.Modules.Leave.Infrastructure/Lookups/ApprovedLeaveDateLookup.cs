using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
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
            .Select(r => new { r.EmployeeId, r.StartDate, r.EndDate, r.LeaveTypeId, r.CalculatedDays, r.StartTime, r.EndTime, r.HalfDayPeriod, ChangedAt = r.ModifiedDate ?? r.CreatedAt })
            .ToListAsync(cancellationToken);

        var types = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.IsPaid, t.Code, ChangedAt = t.ModifiedDate ?? t.CreatedAt })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var result = new List<ApprovedLeaveDay>();
        foreach (var request in requests)
        {
            var start = request.StartDate < from ? from : request.StartDate;
            var end = request.EndDate > to ? to : request.EndDate;
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                var type = types.GetValueOrDefault(request.LeaveTypeId);
                var fraction = request.StartTime is not null || request.HalfDayPeriod is not null
                    ? Math.Clamp(request.CalculatedDays, 0m, 1m) : 1m;
                var changedAt = type is not null && type.ChangedAt > request.ChangedAt ? type.ChangedAt : request.ChangedAt;
                var startsAt = request.StartTime ?? (request.HalfDayPeriod is null ? null
                    : request.HalfDayPeriod == HalfDayPeriod.Morning ? TimeOnly.MinValue : new TimeOnly(12, 0));
                var endsAt = request.EndTime ?? (request.HalfDayPeriod is null ? null
                    : request.HalfDayPeriod == HalfDayPeriod.Morning ? new TimeOnly(12, 0) : TimeOnly.MaxValue);
                result.Add(new ApprovedLeaveDay(request.EmployeeId, date, type?.IsPaid ?? false, fraction,
                    type?.Code == LeaveTypeCode.Sick, changedAt, startsAt, endsAt));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ApprovedLeaveDayDetail>> GetApprovedLeaveDaysWithTypeAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var requests = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= to && r.EndDate >= from)
            .OrderBy(r => r.StartDate)
            .Select(r => new { r.StartDate, r.EndDate, r.LeaveTypeId })
            .ToListAsync(cancellationToken);
        if (requests.Count == 0)
        {
            return [];
        }

        // Unmasked names only - same rule as LeaveCalendar.IsMasked (sensitive, hidden from the calendar, or unknown type).
        var typeIds = requests.Select(r => r.LeaveTypeId).Distinct().ToList();
        var names = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && typeIds.Contains(t.Id) && !t.IsSensitive && t.ShowTypeNameOnCalendar)
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var byDate = new SortedDictionary<DateOnly, string?>();
        foreach (var request in requests)
        {
            var name = names.GetValueOrDefault(request.LeaveTypeId);
            var start = request.StartDate < from ? from : request.StartDate;
            var end = request.EndDate > to ? to : request.EndDate;
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                if (byDate.GetValueOrDefault(date) is null)
                {
                    byDate[date] = name;
                }
            }
        }

        return byDate.Select(d => new ApprovedLeaveDayDetail(d.Key, d.Value)).ToList();
    }
}
