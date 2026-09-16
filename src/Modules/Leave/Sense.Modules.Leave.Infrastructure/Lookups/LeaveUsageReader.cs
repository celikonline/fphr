using Microsoft.EntityFrameworkCore;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Leave.Contracts.ILeaveUsageReader (S-18): aggregates leave.leave_ledger_entries - Accrual/CarryOver dated inside the
/// year for the Annual-coded type's <see cref="LeaveUsageSummary.TotalEntitledDays"/>, Usage/Reversal dated inside the
/// year (netted) for <see cref="LeaveUsageSummary.UsedDays"/>/<see cref="LeaveUsageSummary.ByLeaveType"/>. A tenant can
/// have more than one rule set, so more than one LeaveType with Code=Annual; both count toward the total.
/// </summary>
public sealed class LeaveUsageReader(LeaveDbContext context) : ILeaveUsageReader
{
    public async Task<LeaveUsageSummary> GetAnnualUsageAsync(
        Guid tenantId, int year, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var types = await context.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Code, t.Name })
            .ToListAsync(cancellationToken);
        var typeById = types.ToDictionary(t => t.Id);
        var annualTypeIds = types.Where(t => t.Code == LeaveTypeCode.Annual).Select(t => t.Id).ToHashSet();

        var ledger = context.LedgerEntries.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (employeeIds is not null)
        {
            var ids = employeeIds.ToList();
            ledger = ledger.Where(e => ids.Contains(e.EmployeeId));
        }

        var usageRows = await ledger
            .Where(e => (e.Type == LeaveLedgerEntryType.Usage || e.Type == LeaveLedgerEntryType.Reversal) && e.EffectiveDate >= yearStart && e.EffectiveDate <= yearEnd)
            .Select(e => new { e.LeaveTypeId, e.Amount })
            .ToListAsync(cancellationToken);

        var usedByType = usageRows
            .GroupBy(r => r.LeaveTypeId)
            .ToDictionary(g => g.Key, g => -g.Sum(r => r.Amount));

        var usedDaysAnnual = annualTypeIds.Sum(id => usedByType.GetValueOrDefault(id));

        var totalEntitledDays = annualTypeIds.Count == 0
            ? 0m
            : await ledger
                .Where(e => annualTypeIds.Contains(e.LeaveTypeId) && (e.Type == LeaveLedgerEntryType.Accrual || e.Type == LeaveLedgerEntryType.CarryOver)
                    && e.EffectiveDate >= yearStart && e.EffectiveDate <= yearEnd)
                .SumAsync(e => e.Amount, cancellationToken);

        var byLeaveType = usedByType
            .Where(kv => kv.Value != 0m)
            .Select(kv => new LeaveTypeUsage(kv.Key, typeById.GetValueOrDefault(kv.Key)?.Name ?? string.Empty, kv.Value))
            .OrderBy(u => u.LeaveTypeName, StringComparer.Ordinal)
            .ToList();

        return new LeaveUsageSummary(totalEntitledDays, usedDaysAnnual, byLeaveType);
    }
}
