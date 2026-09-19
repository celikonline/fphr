using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Implements Leave.Contracts.ILeaveStartDateReader (senseik-027, roadmap #71/P-19): approved leave.leave_requests
/// whose StartDate falls in [from, to], excluding sensitive types (LeaveType.IsSensitive) entirely - mirrors
/// LeaveDayReader's direct-EF-query style (no repository round-trip needed for a read-only projection).
/// </summary>
public sealed class LeaveStartDateReader(LeaveDbContext context) : ILeaveStartDateReader
{
    public async Task<IReadOnlyList<LeaveStartDate>> GetApprovedLeaveStartsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var sensitiveTypeIds = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsSensitive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var rows = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == LeaveRequestStatus.Approved
                && r.StartDate >= from && r.StartDate <= to && !sensitiveTypeIds.Contains(r.LeaveTypeId))
            .Select(r => new LeaveStartDate(r.EmployeeId, r.StartDate))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
