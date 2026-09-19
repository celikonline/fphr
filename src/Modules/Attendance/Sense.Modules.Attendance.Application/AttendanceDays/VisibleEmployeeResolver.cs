using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>
/// The "which employees can this caller see" resolution shared by every team-wide Attendance screen (haftalık/aylık
/// ızgara, canlı içeride/dışarıda panosu S-31): data-scope-visible employees, narrowed to one org unit when given,
/// falling back to every active employee when the caller's scope is unrestricted (Company).
/// </summary>
internal static class VisibleEmployeeResolver
{
    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        string permissionKey, Guid? orgUnitId, Guid tenantId, IDataScopeService dataScope, IEmployeeDirectory employees, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(permissionKey, cancellationToken);
        if (orgUnitId is { } unitId)
        {
            var inUnit = await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenantId, [unitId], cancellationToken);
            visible = visible is null ? inUnit.ToHashSet() : inUnit.Where(visible.Contains).ToHashSet();
        }

        if (visible is { Count: 0 })
        {
            return [];
        }

        return visible is null
            ? await employees.ListActiveEmployeeIdsAsync(tenantId, cancellationToken)
            : visible.ToList();
    }
}
