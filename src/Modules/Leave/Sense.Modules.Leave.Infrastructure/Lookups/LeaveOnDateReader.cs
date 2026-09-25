using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Leave.Contracts.ILeaveOnDateReader (zaman-devam Z1.1): employees with an Approved full-day leave request covering the
/// date. Hourly and half-day requests do not take the employee off the day, so they are not returned. An employee with
/// several overlapping requests appears once. IsPaid comes from the request's leave type (unknown type counts as paid).
/// </summary>
/// <remarks>Filters by the explicit <c>tenantId</c>, not the ambient tenant context, so background callers work too.</remarks>
public sealed class LeaveOnDateReader(LeaveDbContext context) : ILeaveOnDateReader
{
    public async Task<IReadOnlyList<EmployeeLeaveOnDate>> ListAsync(
        Guid tenantId, DateOnly date, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct)
    {
        if (employeeIds is { Count: 0 })
        {
            return [];
        }

        var requests = context.Requests.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= date && r.EndDate >= date && r.StartTime == null && r.HalfDayPeriod == null);
        if (employeeIds is not null)
        {
            var ids = employeeIds.ToList();
            requests = requests.Where(r => ids.Contains(r.EmployeeId));
        }

        var rows = await (
                from request in requests
                join type in context.LeaveTypes.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).AsNoTracking() on request.LeaveTypeId equals type.Id into types
                from type in types.DefaultIfEmpty()
                orderby request.EmployeeId, request.StartDate
                select new { request.EmployeeId, Code = request.LeaveTypeCodeSnapshot, IsPaid = type == null || type.IsPaid })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.EmployeeId)
            .Select(g => new EmployeeLeaveOnDate(g.Key, g.First().Code.ToString(), g.First().IsPaid))
            .ToList();
    }
}
