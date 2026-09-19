using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// Rows + dynamic org columns for Attendance's reports: caller's data scope for the report permission ∩ Kolay İK
/// "Birimler" filter (selected units + descendants) ∩ optional "işten ayrılanları dahil et". Employee identity/placement
/// comes through Employee.Contracts (IEmployeeReportReader) - Attendance never reads employee tables. Mirrors
/// Leave/Employee.Infrastructure.Reporting.ReportEmployeeSet (each module keeps its own copy, Modules_TalkOnlyThroughContracts).
/// </summary>
internal sealed class ReportEmployeeSet(IDataScopeService dataScope, IOrgUnitLookup orgUnits, IEmployeeReportReader employees)
{
    public async Task<(IReadOnlyList<EmployeeReportRow> Rows, OrgUnitHierarchy Hierarchy)> LoadAsync(
        Guid tenantId, string scopePermission, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(scopePermission, cancellationToken);
        var selectedUnits = ReportParameters.GetGuidList(parameters, ReportParameterKeys.OrgUnitIds);
        var unitFilter = selectedUnits.Count == 0 ? null : await orgUnits.GetSelfAndDescendantIdsAsync(tenantId, selectedUnits, cancellationToken);
        var includeTerminated = ReportParameters.GetBool(parameters, ReportParameterKeys.IncludeTerminated);

        var hierarchy = new OrgUnitHierarchy(
            await orgUnits.ListActiveTypesAsync(tenantId, cancellationToken),
            await orgUnits.ListActiveUnitsAsync(tenantId, cancellationToken));

        var rows = unitFilter is { Count: 0 }
            ? []
            : await employees.ListAsync(tenantId, visible, unitFilter, includeTerminated, cancellationToken);

        return (rows, hierarchy);
    }
}
