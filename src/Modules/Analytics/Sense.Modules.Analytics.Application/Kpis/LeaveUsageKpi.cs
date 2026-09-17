using Sense.Modules.Analytics.Contracts;
using Sense.Modules.Employee.Contracts;
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
/// S-15 (roadmap #99 "izin kullanım oranı"). <paramref name="OrgUnitId"/> verilirse Employee.Contracts'ın mevcut
/// <see cref="IEmployeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync"/> portuyla (tur içi ek iş md.8) o birimin
/// aktif çalışan id listesine daraltılır; kısıtlı kapsamdaki çağıran için kendi <see cref="IDataScopeService"/>
/// görünürlüğüyle kesişim alınır.
/// </summary>
[RequiresPermission("analytics.kpi.view")]
public sealed record GetLeaveUsageKpiQuery(int? Year = null, Guid? OrgUnitId = null) : IQuery<LeaveUsageKpiDto>;

/// <summary>
/// G-10-17: yalnız sorgu anında <c>Active</c> ve kapsam dahilindeki çalışanları sayar; dönem içinde
/// ayrılan/askıya alınan çalışanın o dönemdeki verisi dahil edilmez (dönem-farkındalıklı sorgu backlog B-10-03).
/// </summary>
public sealed class GetLeaveUsageKpiHandler(
    ITenantContext tenant, ILeaveUsageReader leaveUsage, IEmployeeDirectory employeeDirectory, IDataScopeService dataScope, TimeProvider clock)
    : IQueryHandler<GetLeaveUsageKpiQuery, LeaveUsageKpiDto>
{
    public async Task<Result<LeaveUsageKpiDto>> Handle(GetLeaveUsageKpiQuery query, CancellationToken cancellationToken)
    {
        var year = query.Year ?? clock.GetUtcNow().UtcDateTime.Year;

        // Same IDataScopeService pattern as the workforce KPI (GetWorkforceKpisHandler): null = Company scope
        // (unrestricted, "her çalışan"), a non-null set narrows to exactly what the caller may see.
        var visibleEmployeeIds = await dataScope.GetVisibleEmployeeIdsAsync(AnalyticsPermissions.KpiView.Key, cancellationToken);

        IReadOnlyCollection<Guid>? employeeIds = visibleEmployeeIds;
        if (query.OrgUnitId is { } orgUnitId)
        {
            // G-10-16: ListActiveEmployeeIdsInOrgUnitsAsync is broader than "Active" (excludes only Terminated),
            // unlike ListActiveEmployeeIdsAsync - intersect so the org-unit branch counts the same "Active" set
            // as the company-wide branch (one extra query, at most once per call).
            var orgUnitEmployeeIds = await employeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId], cancellationToken);
            var activeEmployeeIds = await employeeDirectory.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
            var active = new HashSet<Guid>(activeEmployeeIds);
            var activeOrgUnitEmployeeIds = orgUnitEmployeeIds.Where(active.Contains);
            employeeIds = visibleEmployeeIds is null ? activeOrgUnitEmployeeIds.ToList() : activeOrgUnitEmployeeIds.Where(visibleEmployeeIds.Contains).ToList();
        }

        var summary = await leaveUsage.GetAnnualUsageAsync(tenant.TenantId, year, employeeIds, cancellationToken);

        var usageRate = summary.TotalEntitledDays == 0m ? 0m : Math.Round(summary.UsedDays / summary.TotalEntitledDays, 4);
        var byLeaveType = summary.ByLeaveType.Select(t => new LeaveUsageByTypeDto(t.LeaveTypeName, t.UsedDays)).ToList();

        return Result.Success(new LeaveUsageKpiDto(summary.TotalEntitledDays, summary.UsedDays, usageRate, byLeaveType));
    }
}
