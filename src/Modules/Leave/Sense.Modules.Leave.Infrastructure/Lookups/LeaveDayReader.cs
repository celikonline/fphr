using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Leave.Contracts.ILeaveDayReader (S-11): Approved leave.leave_requests fully contained in [from, to], grouped by
/// employee and the leave type's Code bucket (Sick → Raporlu, Unpaid/PostnatalUnpaid → Ücretsiz izin, else → Ücretli
/// izin). Mirrors LeaveUsageReader's direct-EF-query style (no repository round-trip needed for a read-only aggregate).
/// </summary>
public sealed class LeaveDayReader(LeaveDbContext context) : ILeaveDayReader
{
    private static readonly LeaveTypeCode[] UnpaidCodes = [LeaveTypeCode.Unpaid, LeaveTypeCode.PostnatalUnpaid];

    public async Task<IReadOnlyList<LeaveDaySummary>> GetMonthlySummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToList();
        var codeByTypeId = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Code })
            .ToDictionaryAsync(t => t.Id, t => t.Code, cancellationToken);

        var rows = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved
                && r.StartDate >= from && r.EndDate <= to)
            .Select(r => new { r.EmployeeId, r.LeaveTypeId, r.CalculatedDays })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.EmployeeId)
            .Select(group =>
            {
                decimal paid = 0, unpaid = 0, sick = 0;
                foreach (var row in group)
                {
                    var code = codeByTypeId.GetValueOrDefault(row.LeaveTypeId, LeaveTypeCode.Custom);
                    if (code == LeaveTypeCode.Sick)
                    {
                        sick += row.CalculatedDays;
                    }
                    else if (UnpaidCodes.Contains(code))
                    {
                        unpaid += row.CalculatedDays;
                    }
                    else
                    {
                        paid += row.CalculatedDays;
                    }
                }

                return new LeaveDaySummary(group.Key, paid, unpaid, sick);
            })
            .ToList();
    }
}
