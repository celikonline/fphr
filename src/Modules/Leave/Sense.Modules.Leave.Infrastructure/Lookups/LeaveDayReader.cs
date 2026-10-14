using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>Approved leave intersecting a period, allocated by the original request's counted dates.
/// Month boundaries preserve the stored total; custom leave types use their paid setting.</summary>
public sealed class LeaveDayReader(LeaveDbContext context, ILeaveDayCalculator dayCalculator) : ILeaveDayReader
{
    public async Task<IReadOnlyList<LeaveDaySummary>> GetMonthlySummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0 || to < from) return [];
        var ids = employeeIds.ToList();
        var types = await context.LeaveTypes.AsNoTracking().Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Code, t.IsPaid, t.CountsNonWorkingDays })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var rows = await context.Requests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ids.Contains(r.EmployeeId) && r.Status == LeaveRequestStatus.Approved
                && r.StartDate <= to && r.EndDate >= from)
            .Select(r => new { r.EmployeeId, r.LeaveTypeId, r.StartDate, r.EndDate, r.CalculatedDays })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<Guid, LeaveDaySummary>();
        foreach (var row in rows)
        {
            var type = types.GetValueOrDefault(row.LeaveTypeId);
            var days = row.CalculatedDays;
            if (row.StartDate < from || row.EndDate > to)
            {
                var count = await dayCalculator.CalculateAsync(tenantId, row.EmployeeId, row.StartDate, row.EndDate,
                    type?.CountsNonWorkingDays ?? false, cancellationToken);
                if (count.Days <= 0m) continue;
                decimal Weight(DateOnly boundary) => count.Breakdown.Where(d => d.Counted && d.Date <= boundary)
                    .Sum(d => d.HalfDayHoliday ? 0.5m : 1m);
                // Difference of cumulative allocations keeps the sum exact across adjacent months.
                days = decimal.Round(row.CalculatedDays * Weight(to) / count.Days, 4, MidpointRounding.AwayFromZero)
                    - decimal.Round(row.CalculatedDays * (from == DateOnly.MinValue ? 0m : Weight(from.AddDays(-1))) / count.Days, 4, MidpointRounding.AwayFromZero);
            }
            var prior = result.GetValueOrDefault(row.EmployeeId, new LeaveDaySummary(row.EmployeeId, 0m, 0m, 0m));
            result[row.EmployeeId] = type?.Code == LeaveTypeCode.Sick
                ? prior with { SickLeaveDays = prior.SickLeaveDays + days }
                : type?.IsPaid == false
                    ? prior with { UnpaidLeaveDays = prior.UnpaidLeaveDays + days }
                    : prior with { PaidLeaveDays = prior.PaidLeaveDays + days };
        }
        return result.Values.ToList();
    }
}
