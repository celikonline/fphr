using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Turns an <see cref="EmployeeFilter"/>/<see cref="EmployeeSelection"/> into criteria scoped to the caller's visible employees.</summary>
public sealed class EmployeeSelectionResolver(IEmployeeReadStore store, IOrgUnitLookup orgUnits, IDataScopeService dataScope)
{
    public static bool TryParseStatus(string? status, out EmployeeStatus? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        if (Enum.TryParse<EmployeeStatus>(status.Trim(), ignoreCase: true, out var value) && Enum.IsDefined(value))
        {
            parsed = value;
            return true;
        }

        return false;
    }

    /// <summary>Null when the filter can match nothing (e.g. unknown status).</summary>
    public async Task<EmployeeCriteria?> BuildCriteriaAsync(Guid tenantId, EmployeeFilter? filter, string permission, CancellationToken ct)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(permission, ct).ConfigureAwait(false);
        if (filter is null)
        {
            return new EmployeeCriteria(VisibleIds: visible);
        }

        if (!TryParseStatus(filter.Status, out var status))
        {
            return null;
        }

        var requestedUnits = new List<Guid>();
        if (filter.OrgUnitId is { } orgUnitId)
        {
            requestedUnits.Add(orgUnitId);
        }

        if (filter.OrgUnitIds is { Count: > 0 } extraUnits)
        {
            requestedUnits.AddRange(extraUnits);
        }

        IReadOnlyCollection<Guid>? orgUnitIds = null;
        if (requestedUnits.Count > 0)
        {
            var distinctUnits = requestedUnits.Distinct().ToList();
            orgUnitIds = filter.IncludeSubUnits
                ? await orgUnits.GetSelfAndDescendantIdsAsync(tenantId, distinctUnits, ct).ConfigureAwait(false)
                : distinctUnits;
        }

        // Unknown enum text is ignored rather than rejected: the grid's own selects can only send valid values, and a
        // hand-written query string should not 400 the whole list.
        var employmentType = Enum.TryParse<EmploymentType>(filter.EmploymentType, ignoreCase: true, out var parsedEmployment) && Enum.IsDefined(parsedEmployment)
            ? parsedEmployment
            : (EmploymentType?)null;
        var contractType = Enum.TryParse<ContractType>(filter.ContractType, ignoreCase: true, out var parsedContract) && Enum.IsDefined(parsedContract)
            ? parsedContract
            : (ContractType?)null;

        return new EmployeeCriteria(
            Q: filter.Q,
            OrgUnitIds: orgUnitIds,
            Status: status,
            HasUserAccount: filter.HasUserAccount,
            ManagerEmployeeId: filter.ManagerEmployeeId,
            VisibleIds: visible,
            EmploymentType: employmentType,
            ContractType: contractType,
            MissingInfo: filter.MissingInfo,
            ActiveOnly: !filter.IncludeTerminated && status is null);
    }

    public async Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, EmployeeSelection selection, string permission, CancellationToken ct)
    {
        if (selection.EmployeeIds is null && selection.Filter is null)
        {
            return [];
        }

        var criteria = await BuildCriteriaAsync(tenantId, selection.Filter, permission, ct).ConfigureAwait(false);
        if (criteria is null)
        {
            return [];
        }

        if (selection.EmployeeIds is not null)
        {
            if (selection.EmployeeIds.Count == 0)
            {
                return [];
            }

            criteria = criteria with { Ids = selection.EmployeeIds.Distinct().ToList() };
        }

        var ids = await store.ListIdsAsync(criteria, ct).ConfigureAwait(false);
        if (selection.ExcludedEmployeeIds is not { Count: > 0 } excluded)
        {
            return ids;
        }

        var excludedSet = excluded.ToHashSet();
        return ids.Where(id => !excludedSet.Contains(id)).ToList();
    }
}
