using System.Globalization;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.ValueObjects;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Application.Import;

/// <summary>
/// Applies "employee.master" rows. Two phases: every row is validated against a snapshot (existing employees by national id,
/// active managers by full name, active org units by level) before any entity is touched; then valid rows are applied and saved
/// in one Employee SaveChanges. AllOrNothing saves nothing when any row fails. Respects the caller's data scope for
/// employee.employees.import (visible employees, accessible target units).
/// </summary>
public sealed class EmployeeImportRowHandler(
    ITenantContext tenant,
    IEmployeeReadStore store,
    IEmployeeRepository employees,
    IEmployeeUnitOfWork unitOfWork,
    IOrgUnitLookup orgUnits,
    IDataScopeService dataScope,
    CareerService career,
    ICustomFieldDefinitionRepository customFields,
    EmployeeSensitiveAccess sensitiveAccess) : IImportRowHandler
{
    public string TemplateCode => EmployeeImportColumns.TemplateCode;

    public async Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var validator = new EmployeeImportRowValidator(batch.Mode, snapshot);

        var results = new Dictionary<int, ImportRowResult>();
        var plans = new List<EmployeeImportPlan>();
        foreach (var row in batch.Rows)
        {
            var outcome = validator.Validate(row);
            if (outcome.Plan is { } plan)
            {
                plans.Add(plan);
            }
            else
            {
                results[row.RowNumber] = new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, outcome.MatchKey, outcome.ExistingId, outcome.Errors);
            }
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Count > 0)
        {
            return NotApplied(batch, results, plans);
        }

        var applied = await ApplyAsync(plans, results, snapshot.Placement, cancellationToken).ConfigureAwait(false);
        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Values.Any(r => r.Outcome == ImportRowOutcome.Error))
        {
            return NotApplied(batch, results, plans.Where(p => results[p.RowNumber].Outcome != ImportRowOutcome.Error).ToList());
        }

        if (applied)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: true);
    }

    /// <remarks>
    /// Unit/manager changes go through the career timeline like every other write path (new employee: initial Hire
    /// record; existing: amend a record starting today or open a new one effective today). Their CareerChanged /
    /// ManagerChanged events land in the Employee outbox and are only persisted by the SaveChanges below, so a batch
    /// that is not applied publishes nothing.
    /// </remarks>
    private async Task<bool> ApplyAsync(List<EmployeeImportPlan> plans, Dictionary<int, ImportRowResult> results, OrgPlacement placement, CancellationToken ct)
    {
        var updateIds = plans.Where(p => p.Existing is not null && p.HasChanges).Select(p => p.Existing!.Id).ToList();
        // Salary/custom field values are not auto-included (see IEmployeeRepository); a batch may touch either on any
        // updated employee, so every tracked target loads both collections regardless (batch job, not latency-sensitive).
        var tracked = (await employees.ListByIdsWithPaymentsAndCustomFieldsAsync(updateIds, ct).ConfigureAwait(false)).ToDictionary(e => e.Id);
        var managerMap = new Dictionary<Guid, Guid?>(await employees.GetManagerMapAsync(ct).ConfigureAwait(false));
        var context = career.BuildContext(placement, managerMap);
        var anyChange = false;

        foreach (var plan in plans)
        {
            if (plan.Existing is null)
            {
                var created = EmployeeEntity.Create(tenant.TenantId, plan.Info, userId: null, plan.OrgUnitId, plan.ManagerId, plan.NewId);
                if (created.IsFailure)
                {
                    results[plan.RowNumber] = Failed(plan, created.Error.Code);
                    continue;
                }

                if (plan.Salary is { } newSalary)
                {
                    var added = created.Value.AddSalaryRecord(newSalary);
                    if (added.IsFailure)
                    {
                        results[plan.RowNumber] = Failed(plan, added.Error.Code);
                        continue;
                    }
                }

                if (plan.CustomFieldValues is { Count: > 0 } newCustomFields)
                {
                    created.Value.SetCustomFieldValues(newCustomFields);
                }

                employees.Add(created.Value);
                career.PublishInitial(created.Value, created.Value.EnsureInitialCareerRecord(placement.ChainFor(plan.OrgUnitId), context.Today));
                managerMap[plan.NewId] = plan.ManagerId;
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Inserted, plan.NationalId, plan.NewId, []);
                anyChange = true;
                continue;
            }

            if (!plan.HasChanges || !tracked.TryGetValue(plan.Existing.Id, out var employee))
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, plan.Existing.Id, []);
                continue;
            }

            career.Materialize(employee, placement);
            var updated = employee.UpdateInfo(plan.Info);
            if (updated.IsFailure)
            {
                results[plan.RowNumber] = Failed(plan, updated.Error.Code);
                continue;
            }

            var orgUnitChanged = plan.SetOrgUnit && plan.OrgUnitId != employee.OrgUnitId;
            var managerChanged = plan.SetManager && plan.ManagerId != employee.ManagerEmployeeId;
            if (orgUnitChanged || managerChanged)
            {
                var latest = employee.CareerTimeline.LastOrDefault();
                var moved = employee.ApplyPlacement(
                    new PlacementRequest(
                        orgUnitChanged || latest is null ? placement.ChainFor(plan.SetOrgUnit ? plan.OrgUnitId : employee.OrgUnitId) : latest.AssignmentInputs(),
                        managerChanged ? plan.ManagerId : employee.ManagerEmployeeId,
                        latest is null ? employee.EmploymentType : latest.EmploymentType,
                        managerChanged && !orgUnitChanged ? CareerChangeType.ManagerChange : CareerChangeType.Other),
                    context);
                if (moved.IsFailure)
                {
                    results[plan.RowNumber] = Failed(plan, moved.Error.Code);
                    continue;
                }

                career.Publish(employee, moved.Value);
                managerMap[employee.Id] = employee.ManagerEmployeeId;
            }
            else
            {
                // A Draft that just received its first hire date gets its initial record now.
                career.Materialize(employee, placement);
            }

            if (plan.Salary is { } salary)
            {
                var added = employee.AddSalaryRecord(salary);
                if (added.IsFailure)
                {
                    results[plan.RowNumber] = Failed(plan, added.Error.Code);
                    continue;
                }
            }

            if (plan.CustomFieldValues is { Count: > 0 } customFieldValues)
            {
                employee.SetCustomFieldValues(customFieldValues);
            }

            results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Updated, plan.NationalId, employee.Id, []);
            anyChange = true;
        }

        return anyChange;
    }

    private async Task<EmployeeImportSnapshot> LoadSnapshotAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var rows = await store.ListAllAsync(new EmployeeCriteria(), ct).ConfigureAwait(false);
        var types = await orgUnits.ListActiveTypesAsync(tenantId, ct).ConfigureAwait(false);
        var units = await orgUnits.ListActiveUnitsAsync(tenantId, ct).ConfigureAwait(false);
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(EmployeePermissions.EmployeesImportKey, ct).ConfigureAwait(false);
        var accessibleUnits = await dataScope.GetAccessibleOrgUnitIdsAsync(EmployeePermissions.EmployeesImportKey, ct).ConfigureAwait(false);
        var customFieldDefinitions = await customFields.ListActiveAsync(ct).ConfigureAwait(false);
        var canViewSensitiveCustomFields = await sensitiveAccess.CanViewAsync(ct).ConfigureAwait(false);
        return new EmployeeImportSnapshot(rows, types, units, visible, accessibleUnits, customFieldDefinitions, canViewSensitiveCustomFields);
    }

    private static ImportRowResult Failed(EmployeeImportPlan plan, string code) =>
        new(plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, plan.Existing?.Id, [new ImportRowError(null, code)]);

    private static ImportBatchResult NotApplied(ImportBatch batch, Dictionary<int, ImportRowResult> results, IEnumerable<EmployeeImportPlan> validPlans)
    {
        foreach (var plan in validPlans)
        {
            results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, plan.Existing?.Id, []);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: false);
    }

    private static IReadOnlyList<ImportRowResult> Ordered(ImportBatch batch, Dictionary<int, ImportRowResult> results) =>
        batch.Rows.Where(r => results.ContainsKey(r.RowNumber)).Select(r => results[r.RowNumber]).ToList();
}

/// <summary>What a valid row will do: insert (Existing null) or update the listed fields of an existing employee.</summary>
public sealed record EmployeeImportPlan(
    int RowNumber,
    string NationalId,
    EmployeeRow? Existing,
    Guid NewId,
    EmployeeInfo Info,
    bool SetOrgUnit,
    Guid? OrgUnitId,
    bool SetManager,
    Guid? ManagerId,
    bool HasChanges,
    SalaryRecordData? Salary = null,
    IReadOnlyDictionary<Guid, string?>? CustomFieldValues = null);

public sealed record EmployeeImportValidation(EmployeeImportPlan? Plan, string? MatchKey, Guid? ExistingId, IReadOnlyList<ImportRowError> Errors);

/// <summary>Tenant state the rows are validated against (loaded once per batch).</summary>
public sealed class EmployeeImportSnapshot
{
    public EmployeeImportSnapshot(
        IReadOnlyList<EmployeeRow> employees,
        IReadOnlyList<OrgUnitTypeInfo> types,
        IReadOnlyList<OrgUnitInfo> units,
        IReadOnlySet<Guid>? visibleEmployeeIds,
        IReadOnlySet<Guid>? accessibleOrgUnitIds,
        IReadOnlyList<CustomFieldDefinition> customFieldDefinitions,
        bool canViewSensitiveCustomFields)
    {
        ByNationalId = employees.Where(e => e.NationalId is not null).GroupBy(e => e.NationalId!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        // Status rename (Faz 1a): Inactive -> Terminated is the closest equivalent to "not usable as a manager match".
        ActiveByFullName = employees.Where(e => e.Status != EmployeeStatus.Terminated).ToLookup(e => ImportValueParser.MatchKey(e.DisplayName));
        ManagerMap = employees.ToDictionary(e => e.Id, e => e.ManagerEmployeeId);
        Types = types.OrderBy(t => t.SortOrder).ToList();
        UnitsByType = units.GroupBy(u => u.TypeId).ToDictionary(g => g.Key, g => g.ToLookup(u => ImportValueParser.MatchKey(u.Name)));
        ParentOf = units.ToDictionary(u => u.Id, u => u.ParentId);
        VisibleEmployeeIds = visibleEmployeeIds;
        AccessibleOrgUnitIds = accessibleOrgUnitIds;
        Placement = new OrgPlacement(types, units);
        CustomFieldDefinitions = customFieldDefinitions;
        CanViewSensitiveCustomFields = canViewSensitiveCustomFields;
    }

    public OrgPlacement Placement { get; }

    /// <summary>Active custom field definitions ("customField:{Key}" columns), loaded once per batch.</summary>
    public IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions { get; }

    /// <summary>Whether the caller holds employee.sensitive.view - gates whether a sensitive custom field's cell is applied (same rule as <see cref="Sense.Modules.Employee.Application.Employees.SetEmployeeCustomFieldsHandler"/>).</summary>
    public bool CanViewSensitiveCustomFields { get; }

    public IReadOnlyDictionary<string, EmployeeRow> ByNationalId { get; }

    public ILookup<string, EmployeeRow> ActiveByFullName { get; }

    /// <summary>Simulated manager hierarchy, updated as rows pass validation so cycles across rows are caught.</summary>
    public Dictionary<Guid, Guid?> ManagerMap { get; }

    public IReadOnlyList<OrgUnitTypeInfo> Types { get; }

    public IReadOnlyDictionary<Guid, ILookup<string, OrgUnitInfo>> UnitsByType { get; }

    public IReadOnlyDictionary<Guid, Guid?> ParentOf { get; }

    public IReadOnlySet<Guid>? VisibleEmployeeIds { get; }

    public IReadOnlySet<Guid>? AccessibleOrgUnitIds { get; }

    public bool IsDescendantOf(Guid unitId, Guid ancestorId)
    {
        var visited = new HashSet<Guid>();
        Guid? current = unitId;
        while (current is { } id && visited.Add(id))
        {
            if (id == ancestorId)
            {
                return true;
            }

            current = ParentOf.TryGetValue(id, out var parent) ? parent : null;
        }

        return false;
    }
}
