using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
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
    IDataScopeService dataScope) : IImportRowHandler
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

        var applied = await ApplyAsync(plans, results, cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> ApplyAsync(List<EmployeeImportPlan> plans, Dictionary<int, ImportRowResult> results, CancellationToken ct)
    {
        var updateIds = plans.Where(p => p.Existing is not null && p.HasChanges).Select(p => p.Existing!.Id).ToList();
        var tracked = (await employees.ListByIdsAsync(updateIds, ct).ConfigureAwait(false)).ToDictionary(e => e.Id);
        var managerMap = new Dictionary<Guid, Guid?>(await employees.GetManagerMapAsync(ct).ConfigureAwait(false));
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

                employees.Add(created.Value);
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

            if (plan.SetManager && plan.ManagerId != employee.ManagerEmployeeId)
            {
                var assigned = employee.AssignManager(plan.ManagerId, managerMap);
                if (assigned.IsFailure)
                {
                    results[plan.RowNumber] = Failed(plan, assigned.Error.Code);
                    continue;
                }

                managerMap[employee.Id] = plan.ManagerId;
            }

            var updated = employee.UpdateInfo(plan.Info);
            if (updated.IsFailure)
            {
                results[plan.RowNumber] = Failed(plan, updated.Error.Code);
                continue;
            }

            if (plan.SetOrgUnit)
            {
                employee.AssignOrgUnit(plan.OrgUnitId);
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
        return new EmployeeImportSnapshot(rows, types, units, visible, accessibleUnits);
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
    bool HasChanges);

public sealed record EmployeeImportValidation(EmployeeImportPlan? Plan, string? MatchKey, Guid? ExistingId, IReadOnlyList<ImportRowError> Errors);

/// <summary>Tenant state the rows are validated against (loaded once per batch).</summary>
public sealed class EmployeeImportSnapshot
{
    public EmployeeImportSnapshot(
        IReadOnlyList<EmployeeRow> employees,
        IReadOnlyList<OrgUnitTypeInfo> types,
        IReadOnlyList<OrgUnitInfo> units,
        IReadOnlySet<Guid>? visibleEmployeeIds,
        IReadOnlySet<Guid>? accessibleOrgUnitIds)
    {
        ByNationalId = employees.Where(e => e.NationalId is not null).GroupBy(e => e.NationalId!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        ActiveByFullName = employees.Where(e => e.Status != EmployeeStatus.Inactive).ToLookup(e => ImportValueParser.MatchKey(e.DisplayName));
        ManagerMap = employees.ToDictionary(e => e.Id, e => e.ManagerEmployeeId);
        Types = types.OrderBy(t => t.SortOrder).ToList();
        UnitsByType = units.GroupBy(u => u.TypeId).ToDictionary(g => g.Key, g => g.ToLookup(u => ImportValueParser.MatchKey(u.Name)));
        ParentOf = units.ToDictionary(u => u.Id, u => u.ParentId);
        VisibleEmployeeIds = visibleEmployeeIds;
        AccessibleOrgUnitIds = accessibleOrgUnitIds;
    }

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

/// <summary>Per-row validation of the "employee.master" template (no I/O; state comes from <see cref="EmployeeImportSnapshot"/>).</summary>
public sealed class EmployeeImportRowValidator(ImportMode mode, EmployeeImportSnapshot snapshot)
{
    private readonly Dictionary<string, int> _seenNationalIds = new(StringComparer.Ordinal);

    public EmployeeImportValidation Validate(ImportRow row)
    {
        var errors = new List<ImportRowError>();

        string? nationalId = null;
        var nationalIdCell = row.Get(EmployeeImportColumns.NationalId);
        if (nationalIdCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.RequiredMissing));
        }
        else if (!ImportValueParser.TryParseNationalId(nationalIdCell.Text, out var parsedId))
        {
            errors.Add(ValueError(EmployeeImportColumns.NationalId, ImportErrorCodes.InvalidNationalId, nationalIdCell));
        }
        else
        {
            nationalId = parsedId;
            if (_seenNationalIds.TryGetValue(parsedId, out var firstRow))
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.DuplicateRow,
                    new Dictionary<string, string> { [ImportErrorCodes.RowArg] = firstRow.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
            else
            {
                _seenNationalIds[parsedId] = row.RowNumber;
            }
        }

        var existing = nationalId is not null ? snapshot.ByNationalId.GetValueOrDefault(nationalId) : null;
        if (existing is not null && snapshot.VisibleEmployeeIds is { } visible && !visible.Contains(existing.Id))
        {
            // No further checks: they would reveal data of an employee outside the caller's scope.
            return new EmployeeImportValidation(null, nationalId, null, [new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.OutOfScope)]);
        }

        if (existing is null && nationalId is not null && mode == ImportMode.UpdateOnly)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.NationalId, ImportErrorCodes.NoMatch));
        }

        var firstName = Text(row, EmployeeImportColumns.FirstName);
        var lastName = Text(row, EmployeeImportColumns.LastName);
        if (existing is null)
        {
            if (firstName is null)
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.FirstName, ImportErrorCodes.RequiredMissing));
            }

            if (lastName is null)
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.LastName, ImportErrorCodes.RequiredMissing));
            }
        }
        else if ((firstName is not null && !ImportValueParser.TextEquals(firstName, existing.FirstName)) ||
                 (lastName is not null && !ImportValueParser.TextEquals(lastName, existing.LastName)))
        {
            errors.Add(new ImportRowError(null, ImportErrorCodes.NameMismatch,
                new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = string.Join(' ', new[] { firstName, lastName }.Where(n => n is not null)) }));
        }

        CheckLength(errors, EmployeeImportColumns.FirstName, firstName, EmployeeLimits.NameMaxLength);
        CheckLength(errors, EmployeeImportColumns.LastName, lastName, EmployeeLimits.NameMaxLength);

        var workEmail = Email(row, EmployeeImportColumns.WorkEmail, errors);
        var personalEmail = Email(row, EmployeeImportColumns.PersonalEmail, errors);
        var workPhone = Text(row, EmployeeImportColumns.WorkPhone);
        if (workPhone is { Length: > EmployeeLimits.PhoneMaxLength })
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.WorkPhone, ImportErrorCodes.InvalidPhone, ValueArgs(workPhone)));
        }

        var hireDate = Date(row, EmployeeImportColumns.HireDate, errors);
        var birthDate = Date(row, EmployeeImportColumns.BirthDate, errors);
        var managerId = ResolveManager(row, existing, errors);
        var (setOrgUnit, orgUnitId) = ResolveOrgUnit(row, existing, errors);

        if (errors.Count > 0 || nationalId is null)
        {
            return new EmployeeImportValidation(null, nationalId, existing?.Id, errors);
        }

        var info = existing is null
            ? new EmployeeInfo(firstName!, lastName!, nationalId, workEmail, personalEmail, workPhone, hireDate, birthDate)
            : new EmployeeInfo(
                existing.FirstName,
                existing.LastName,
                existing.NationalId,
                workEmail ?? existing.WorkEmail,
                personalEmail ?? existing.PersonalEmail,
                workPhone ?? existing.WorkPhone,
                hireDate ?? existing.HireDate,
                birthDate ?? existing.BirthDate);

        var domainCheck = EmployeeEntity.Validate(info);
        if (domainCheck.IsFailure)
        {
            return new EmployeeImportValidation(null, nationalId, existing?.Id, [new ImportRowError(null, domainCheck.Error.Code)]);
        }

        var setManager = managerId is not null;
        var newId = existing?.Id ?? Guid.CreateVersion7();
        var hasChanges = existing is null ||
            info != new EmployeeInfo(existing.FirstName, existing.LastName, existing.NationalId, existing.WorkEmail, existing.PersonalEmail, existing.WorkPhone, existing.HireDate, existing.BirthDate) ||
            (setOrgUnit && orgUnitId != existing.OrgUnitId) ||
            (setManager && managerId != existing.ManagerEmployeeId);

        if (setManager)
        {
            snapshot.ManagerMap[newId] = managerId;
        }

        return new EmployeeImportValidation(
            new EmployeeImportPlan(row.RowNumber, nationalId, existing, newId, info, setOrgUnit, orgUnitId, setManager, managerId, hasChanges),
            nationalId,
            existing?.Id,
            []);
    }

    private Guid? ResolveManager(ImportRow row, EmployeeRow? existing, List<ImportRowError> errors)
    {
        var name = Text(row, EmployeeImportColumns.Manager);
        if (name is null)
        {
            return null;
        }

        var candidates = snapshot.ActiveByFullName[ImportValueParser.MatchKey(name)].ToList();
        if (candidates.Count == 0)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.Manager, ImportErrorCodes.LookupNotFound, ValueArgs(name)));
            return null;
        }

        if (candidates.Count > 1)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.Manager, ImportErrorCodes.AmbiguousMatch, ValueArgs(name)));
            return null;
        }

        var managerId = candidates[0].Id;
        if (existing is not null)
        {
            if (managerId == existing.Id)
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.Manager, EmployeeDomainErrors.ManagerSelf));
                return null;
            }

            if (managerId != existing.ManagerEmployeeId && ManagerHierarchy.WouldCreateCycle(existing.Id, managerId, snapshot.ManagerMap))
            {
                errors.Add(new ImportRowError(EmployeeImportColumns.Manager, EmployeeDomainErrors.ManagerCycle));
                return null;
            }
        }

        return managerId;
    }

    /// <summary>Resolves every provided level (names within the level; ambiguity narrowed by shallower provided units); the deepest wins.</summary>
    private (bool Set, Guid? OrgUnitId) ResolveOrgUnit(ImportRow row, EmployeeRow? existing, List<ImportRowError> errors)
    {
        OrgUnitInfo? deepest = null;
        string? deepestKey = null;
        var failed = false;
        foreach (var type in snapshot.Types)
        {
            var key = EmployeeImportColumns.OrgUnit(type.Code);
            var name = Text(row, key);
            if (name is null)
            {
                continue;
            }

            var candidates = snapshot.UnitsByType.TryGetValue(type.Id, out var byName) ? byName[ImportValueParser.MatchKey(name)].ToList() : [];
            if (candidates.Count > 1 && deepest is not null)
            {
                var ancestor = deepest.Id;
                candidates = candidates.Where(c => snapshot.IsDescendantOf(c.Id, ancestor)).ToList();
            }

            if (candidates.Count == 0)
            {
                errors.Add(new ImportRowError(key, ImportErrorCodes.LookupNotFound, ValueArgs(name)));
                failed = true;
            }
            else if (candidates.Count > 1)
            {
                errors.Add(new ImportRowError(key, ImportErrorCodes.AmbiguousMatch, ValueArgs(name)));
                failed = true;
            }
            else
            {
                deepest = candidates[0];
                deepestKey = key;
            }
        }

        if (failed)
        {
            return (false, null);
        }

        if (snapshot.AccessibleOrgUnitIds is { } accessible)
        {
            var targetChanges = deepest is not null && deepest.Id != existing?.OrgUnitId;
            if ((targetChanges && !accessible.Contains(deepest!.Id)) || (existing is null && deepest is null))
            {
                errors.Add(new ImportRowError(deepestKey, ImportErrorCodes.OutOfScope));
                return (false, null);
            }
        }

        return deepest is null ? (false, null) : (true, deepest.Id);
    }

    private static string? Text(ImportRow row, string key)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        var text = ImportValueParser.Normalize(cell.Text ?? cell.DateValue?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture));
        return text.Length == 0 ? null : text;
    }

    private static string? Email(ImportRow row, string key, List<ImportRowError> errors)
    {
        var text = Text(row, key);
        if (text is null)
        {
            return null;
        }

        if (!ImportValueParser.IsValidEmail(text))
        {
            errors.Add(new ImportRowError(key, ImportErrorCodes.InvalidEmail, ValueArgs(text)));
            return null;
        }

        return text.ToLowerInvariant();
    }

    private static DateOnly? Date(ImportRow row, string key, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        if (ImportValueParser.TryParseDate(cell, out var date))
        {
            return date;
        }

        errors.Add(ValueError(key, ImportErrorCodes.InvalidDate, cell));
        return null;
    }

    private static void CheckLength(List<ImportRowError> errors, string key, string? value, int max)
    {
        if (value is { Length: var length } && length > max)
        {
            errors.Add(new ImportRowError(key, ImportErrorCodes.ValueTooLong, new Dictionary<string, string> { ["max"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }
    }

    private static ImportRowError ValueError(string key, string code, ImportCell cell) => new(key, code, ValueArgs(cell.Text?.Trim() ?? string.Empty));

    private static Dictionary<string, string> ValueArgs(string value) => new() { [ImportErrorCodes.ValueArg] = value };
}
