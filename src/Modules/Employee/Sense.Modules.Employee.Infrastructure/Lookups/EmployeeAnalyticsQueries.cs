using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeAnalyticsQueries so the Analytics module can compute workforce KPIs.</summary>
public sealed class EmployeeAnalyticsQueries(EmployeeDbContext context) : IEmployeeAnalyticsQueries
{
    private static readonly CareerChangeType[] CareerPathChangeTypes =
    [
        CareerChangeType.Promotion, CareerChangeType.LateralMove, CareerChangeType.Demotion, CareerChangeType.TitleChange,
    ];

    public async Task<int> GetActiveHeadcountAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.Status == EmployeeStatus.Active, cancellationToken);

    public async Task<CareerPathChangeCounts> GetCareerPathChangeCountsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var counts = await context.Set<CareerRecord>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ValidFrom >= from && r.ValidFrom <= to && CareerPathChangeTypes.Contains(r.ChangeType))
            .GroupBy(r => r.ChangeType)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return new CareerPathChangeCounts(
            counts.GetValueOrDefault(CareerChangeType.Promotion),
            counts.GetValueOrDefault(CareerChangeType.LateralMove),
            counts.GetValueOrDefault(CareerChangeType.Demotion),
            counts.GetValueOrDefault(CareerChangeType.TitleChange));
    }

    public async Task<IReadOnlyDictionary<string, int>> GetGenderHeadcountAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var counts = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == EmployeeStatus.Active)
            .GroupBy(e => e.Gender)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(g => g.Key.ToString(), g => g.Count);
    }
}
