using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeAnalyticsQueries so the Analytics module can compute workforce KPIs.</summary>
public sealed class EmployeeAnalyticsQueries(EmployeeDbContext context, IOrgUnitLookup orgUnits) : IEmployeeAnalyticsQueries
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

    public async Task<TerminationCounts> GetTerminationCountsAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        // Fetched client-side (just two nullable DateOnly columns): DayNumber arithmetic avoids relying on
        // date-subtraction translation, and per-tenant termination volume in one period is small.
        var terminations = await context.Employees.AsNoTracking().IgnoreAutoIncludes()
            .Where(e => e.TenantId == tenantId && e.TerminationDate != null && e.TerminationDate >= from && e.TerminationDate <= to)
            .Select(e => new { e.HireDate, e.TerminationDate })
            .ToListAsync(cancellationToken);

        var withinFirstSixMonths = terminations.Count(t =>
            t.HireDate is not null && t.TerminationDate!.Value.DayNumber - t.HireDate.Value.DayNumber < EarlyAttritionDays);

        return new TerminationCounts(terminations.Count, withinFirstSixMonths);
    }

    public async Task<IReadOnlyList<GenderByOrgUnitCount>> GetGenderByOrgUnitAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var counts = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == EmployeeStatus.Active)
            .GroupBy(e => new { e.OrgUnitId, e.Gender })
            .Select(g => new { g.Key.OrgUnitId, g.Key.Gender, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.Select(c => new GenderByOrgUnitCount(c.OrgUnitId, c.Gender.ToString(), c.Count)).ToList();
    }

    /// <summary>
    /// senseik-046: re-groups the raw per-OrgUnitId counts onto their Department-level ancestor (same org placement
    /// chain resolution as <see cref="OrgPlacement.LevelUnitFor"/>, used by EmployeeQueries for the employee card's
    /// DepartmentName and GetDepartmentDistributionHandler) so employees under different titles in one department
    /// land in one bucket instead of being split - or hidden - behind their (usually Title-level) raw unit.
    /// </summary>
    public async Task<IReadOnlyList<GenderByOrgUnitCount>> GetGenderByDepartmentAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var byRawUnit = await GetGenderByOrgUnitAsync(tenantId, cancellationToken);
        if (byRawUnit.Count == 0)
        {
            return byRawUnit;
        }

        var types = await orgUnits.ListActiveTypesAsync(tenantId, cancellationToken);
        var units = await orgUnits.ListActiveUnitsAsync(tenantId, cancellationToken);
        var placement = new OrgPlacement(types, units);

        return byRawUnit
            .GroupBy(c => (DepartmentId: placement.LevelUnitFor(c.OrgUnitId, "Department")?.Id, c.Gender))
            .Select(g => new GenderByOrgUnitCount(g.Key.DepartmentId, g.Key.Gender, g.Sum(c => c.Count)))
            .ToList();
    }

    private const int EarlyAttritionDays = 182;
}
