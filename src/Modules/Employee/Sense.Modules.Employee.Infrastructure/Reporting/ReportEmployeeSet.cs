using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Infrastructure.Reporting;

/// <summary>
/// The rows and dynamic org columns every Employee-module report starts from: caller's data scope for the report's
/// permission ∩ the Kolay İK "Birimler" filter (selected units + descendants) ∩ the optional "işten ayrılanları dahil
/// et" switch, plus the tenant's hierarchy for the per-level career columns.
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

        // A unit filter that expands to nothing (all inactive/unknown) must yield an empty report, not "everyone".
        var rows = unitFilter is { Count: 0 }
            ? []
            : await employees.ListAsync(tenantId, visible, unitFilter, includeTerminated, cancellationToken);

        return (rows, hierarchy);
    }
}
