using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Analytics.Application.Kpis;

/// <summary>One leave type's approved usage inside the requested year (S-15 breakdown row).</summary>
public sealed record LeaveUsageByTypeDto(string LeaveTypeName, decimal UsedDays);

/// <summary>
/// S-15 izin kullanım KPI'sı: yıllık izin hakkı/kullanımı ve oranı, tüm izin tiplerinin dönem içi kullanım kırılımı
/// (<see cref="ByLeaveType"/>). Veri kaynağı Leave.Contracts.ILeaveUsageReader (S-18).
/// </summary>
public sealed record LeaveUsageKpiDto(decimal TotalEntitledDays, decimal UsedDays, decimal UsageRate, IReadOnlyList<LeaveUsageByTypeDto> ByLeaveType);

/// <summary>
/// S-15 (roadmap #99 "izin kullanım oranı"). <paramref name="OrgUnitId"/> is accepted for shape-symmetry with the
/// other Analytics KPI endpoints but is not yet applied as a narrowing filter - Employee.Contracts has no
/// org-unit -&gt; employee-ids lookup today (see rapor-be-20260916-1754); only the caller's own
/// <see cref="IDataScopeService"/> scope (Team/Department/Location/Company) restricts the result.
/// </summary>
[RequiresPermission("analytics.kpi.view")]
public sealed record GetLeaveUsageKpiQuery(int? Year = null, Guid? OrgUnitId = null) : IQuery<LeaveUsageKpiDto>;

public sealed class GetLeaveUsageKpiHandler(ITenantContext tenant, ILeaveUsageReader leaveUsage, IDataScopeService dataScope, TimeProvider clock)
    : IQueryHandler<GetLeaveUsageKpiQuery, LeaveUsageKpiDto>
{
    public async Task<Result<LeaveUsageKpiDto>> Handle(GetLeaveUsageKpiQuery query, CancellationToken cancellationToken)
    {
        var year = query.Year ?? clock.GetUtcNow().UtcDateTime.Year;

        // Same IDataScopeService pattern as the workforce KPI (GetWorkforceKpisHandler): null = Company scope
        // (unrestricted, "her çalışan"), a non-null set narrows to exactly what the caller may see.
        var visibleEmployeeIds = await dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, cancellationToken);

        var summary = await leaveUsage.GetAnnualUsageAsync(tenant.TenantId, year, visibleEmployeeIds, cancellationToken);

        var usageRate = summary.TotalEntitledDays == 0m ? 0m : Math.Round(summary.UsedDays / summary.TotalEntitledDays, 4);
        var byLeaveType = summary.ByLeaveType.Select(t => new LeaveUsageByTypeDto(t.LeaveTypeName, t.UsedDays)).ToList();

        return Result.Success(new LeaveUsageKpiDto(summary.TotalEntitledDays, summary.UsedDays, usageRate, byLeaveType));
    }
}
