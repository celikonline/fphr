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
        var leaveAccrualStartDate = Date(row, EmployeeImportColumns.LeaveAccrualStartDate, errors);
        var managerId = ResolveManager(row, existing, errors);
        var (setOrgUnit, orgUnitId) = ResolveOrgUnit(row, existing, errors);

        // Personnel fields (E6): an empty cell keeps the stored value, an unknown option is a row error.
        var gender = Enum<Gender>(row, EmployeeImportColumns.Gender, errors);
        var maritalStatus = Enum<MaritalStatus>(row, EmployeeImportColumns.MaritalStatus, errors);
        var childCount = ChildCount(row, errors);
        var educationStatus = Enum<EducationStatus>(row, EmployeeImportColumns.EducationStatus, errors);
        var highestEducationLevel = Enum<HighestEducationLevel>(row, EmployeeImportColumns.HighestEducationLevel, errors);
        var militaryStatus = Enum<MilitaryStatus>(row, EmployeeImportColumns.MilitaryStatus, errors);
        var disabilityDegree = Enum<DisabilityDegree>(row, EmployeeImportColumns.DisabilityDegree, errors);
        var bloodType = Enum<BloodType>(row, EmployeeImportColumns.BloodType, errors);
        var mobilePhone = Text(row, EmployeeImportColumns.MobilePhone);
        var homePhone = Text(row, EmployeeImportColumns.HomePhone);
        var addressLine = Text(row, EmployeeImportColumns.AddressLine);
        var city = Text(row, EmployeeImportColumns.City);
        var postalCode = Text(row, EmployeeImportColumns.PostalCode);
        var contractType = Enum<ContractType>(row, EmployeeImportColumns.ContractType, errors);
        var employmentType = Enum<EmploymentType>(row, EmployeeImportColumns.EmploymentType, errors);
        var salary = Salary(row, errors);
        var customFieldValues = CustomFields(row, errors);

        if (errors.Count > 0 || nationalId is null)
        {
            return new EmployeeImportValidation(null, nationalId, existing?.Id, errors);
        }

        var baseInfo = existing is null
            ? new EmployeeInfo(firstName!, lastName!, nationalId, workEmail, personalEmail, workPhone, hireDate, birthDate)
            : ExistingInfo(existing) with
            {
                WorkEmail = workEmail ?? existing.WorkEmail,
                PersonalEmail = personalEmail ?? existing.PersonalEmail,
                WorkPhone = workPhone ?? existing.WorkPhone,
                HireDate = hireDate ?? existing.HireDate,
                BirthDate = birthDate ?? existing.BirthDate,
            };

        var info = baseInfo with
        {
            LeaveAccrualStartDate = leaveAccrualStartDate,
            Gender = gender ?? baseInfo.Gender,
            MaritalStatus = maritalStatus ?? baseInfo.MaritalStatus,
            ChildCount = childCount ?? baseInfo.ChildCount,
            EducationStatus = educationStatus ?? baseInfo.EducationStatus,
            HighestEducationLevel = highestEducationLevel ?? baseInfo.HighestEducationLevel,
            MilitaryStatus = militaryStatus ?? baseInfo.MilitaryStatus,
            DisabilityDegree = disabilityDegree ?? baseInfo.DisabilityDegree,
            BloodType = bloodType ?? baseInfo.BloodType,
            MobilePhone = mobilePhone ?? baseInfo.MobilePhone,
            HomePhone = homePhone ?? baseInfo.HomePhone,
            AddressLine = addressLine ?? baseInfo.AddressLine,
            City = city ?? baseInfo.City,
            PostalCode = postalCode ?? baseInfo.PostalCode,
            ContractType = contractType ?? baseInfo.ContractType,
            EmploymentType = employmentType ?? baseInfo.EmploymentType,
        };

        var domainCheck = EmployeeEntity.Validate(info);
        if (domainCheck.IsFailure)
        {
            return new EmployeeImportValidation(null, nationalId, existing?.Id, [new ImportRowError(null, domainCheck.Error.Code)]);
        }

        var setManager = managerId is not null;
        var newId = existing?.Id ?? Guid.CreateVersion7();
        var hasChanges = existing is null ||
            info != ExistingInfo(existing) ||
            (setOrgUnit && orgUnitId != existing.OrgUnitId) ||
            (setManager && managerId != existing.ManagerEmployeeId) ||
            salary is not null ||
            customFieldValues.Count > 0;

        if (setManager)
        {
            snapshot.ManagerMap[newId] = managerId;
        }

        return new EmployeeImportValidation(
            new EmployeeImportPlan(row.RowNumber, nationalId, existing, newId, info, setOrgUnit, orgUnitId, setManager, managerId, hasChanges, salary, customFieldValues),
            nationalId,
            existing?.Id,
            []);
    }

    /// <summary>
    /// The stored values of every field the template has no column for, so an update row cannot wipe them.
    /// LeaveAccrualStartDate stays null (= keep, or follow a corrected hire date while it still equals the old one).
    /// </summary>
    private static EmployeeInfo ExistingInfo(EmployeeRow e) => new(
        e.FirstName, e.LastName, e.NationalId, e.WorkEmail, e.PersonalEmail, e.WorkPhone, e.HireDate, e.BirthDate,
        e.Gender, e.MaritalStatus, e.ChildCount, e.EducationStatus, e.HighestEducationLevel, e.MilitaryStatus, e.DisabilityDegree, e.BloodType,
        e.MobilePhone, e.HomePhone, e.AddressLine, e.City, e.PostalCode, e.ContractType, e.EmploymentType);

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

    /// <summary>An enum column (E6): empty keeps the stored value, an unrecognized option is reported as a lookup error.</summary>
    private static T? Enum<T>(ImportRow row, string key, List<ImportRowError> errors)
        where T : struct, Enum
    {
        var text = Text(row, key);
        if (text is null)
        {
            return null;
        }

        if (EmployeeImportOptions.TryParse<T>(text, out var value))
        {
            return value;
        }

        errors.Add(new ImportRowError(key, ImportErrorCodes.LookupNotFound, ValueArgs(text)));
        return null;
    }

    private static int? ChildCount(ImportRow row, List<ImportRowError> errors)
    {
        var text = Text(row, EmployeeImportColumns.ChildCount);
        if (text is null)
        {
            return null;
        }

        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) && count >= 0)
        {
            return count;
        }

        errors.Add(new ImportRowError(EmployeeImportColumns.ChildCount, EmployeeDomainErrors.ChildCountInvalid, ValueArgs(text)));
        return null;
    }

    /// <summary>
    /// "Maaş bloğu bütünlüğü" (11.3.03): without both an effective date and an amount, the whole block is ignored -
    /// not an error, even if other salary cells are filled. Once both are present, every other cell is validated
    /// normally (missing Currency/SalaryType/PayFrequency mirrors <see cref="Payments.SalaryRules"/>'s manual-entry
    /// requiredness; everything else reuses <see cref="SalaryRecord"/>'s own rules via the values it accepts).
    /// </summary>
    private static SalaryRecordData? Salary(ImportRow row, List<ImportRowError> errors)
    {
        if (row.Get(EmployeeImportColumns.SalaryEffectiveFrom) is null || row.Get(EmployeeImportColumns.SalaryAmount) is null)
        {
            return null;
        }

        var effectiveFrom = Date(row, EmployeeImportColumns.SalaryEffectiveFrom, errors);
        var amount = Amount(row, EmployeeImportColumns.SalaryAmount, EmployeeDomainErrors.SalaryAmountInvalid, allowZero: false, errors);

        var currencyCell = row.Get(EmployeeImportColumns.SalaryCurrency);
        string? currency = null;
        if (currencyCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.SalaryCurrency, EmployeeDomainErrors.CurrencyRequired));
        }
        else
        {
            var normalized = NormalizeCurrencyAlias(Text(row, EmployeeImportColumns.SalaryCurrency));
            if (normalized is not null && Currencies.IsKnown(normalized))
            {
                currency = normalized;
            }
            else
            {
                errors.Add(ValueError(EmployeeImportColumns.SalaryCurrency, EmployeeDomainErrors.CurrencyInvalid, currencyCell));
            }
        }

        var salaryTypeCell = row.Get(EmployeeImportColumns.SalaryType);
        var salaryType = Enum<SalaryType>(row, EmployeeImportColumns.SalaryType, errors);
        if (salaryTypeCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.SalaryType, EmployeeDomainErrors.SalaryTypeRequired));
        }

        var payFrequencyCell = row.Get(EmployeeImportColumns.PayFrequency);
        var payFrequency = Enum<PayFrequency>(row, EmployeeImportColumns.PayFrequency, errors);
        if (payFrequencyCell is null)
        {
            errors.Add(new ImportRowError(EmployeeImportColumns.PayFrequency, EmployeeDomainErrors.SalaryPayFrequencyRequired));
        }

        var transportAllowance = Amount(row, EmployeeImportColumns.TransportAllowance, EmployeeDomainErrors.SalaryAllowanceInvalid, allowZero: true, errors);
        var mealAllowance = Amount(row, EmployeeImportColumns.MealAllowance, EmployeeDomainErrors.SalaryAllowanceInvalid, allowZero: true, errors);

        if (effectiveFrom is null || amount is null || currency is null || salaryType is null || payFrequency is null)
        {
            return null;
        }

        return new SalaryRecordData(
            effectiveFrom.Value, amount.Value, currency, salaryType.Value, payFrequency.Value, transportAllowance, mealAllowance, Source: SalarySource.Import);
    }

    /// <summary>Kolay İK kısaltmaları ("TL", "Euro") normalize to their ISO 4217 code before <see cref="Currencies.IsKnown"/>; every other value only gets <see cref="SalaryRecord.NormalizeCurrency"/>'s trim/uppercase.</summary>
    private static readonly Dictionary<string, string> CurrencyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TL"] = Currencies.TRY,
        ["EURO"] = Currencies.EUR,
    };

    private static string? NormalizeCurrencyAlias(string? text) =>
        text is not null && CurrencyAliases.TryGetValue(text, out var iso) ? iso : SalaryRecord.NormalizeCurrency(text);

    /// <summary>A decimal cell (salary amount/allowances): absent keeps the current value (null, no error); present but not a valid amount is a row error.</summary>
    private static decimal? Amount(ImportRow row, string key, string invalidCode, bool allowZero, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        if (decimal.TryParse(cell.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
            SalaryRecord.IsValidAmount(value, allowZero))
        {
            return value;
        }

        errors.Add(ValueError(key, invalidCode, cell));
        return null;
    }

    /// <summary>
    /// One entry per mapped, non-empty "customField:{Key}" column, coerced/validated by the same
    /// <see cref="CustomFieldValueCoercion"/> the HR/self-service write endpoints use. An empty cell keeps the
    /// stored value (omitted from the dictionary); a sensitive definition's cell is silently skipped when the
    /// caller lacks employee.sensitive.view - same precedent as <see cref="SetEmployeeCustomFieldsHandler"/>.
    /// </summary>
    private Dictionary<Guid, string?> CustomFields(ImportRow row, List<ImportRowError> errors)
    {
        var values = new Dictionary<Guid, string?>();
        foreach (var definition in snapshot.CustomFieldDefinitions)
        {
            var key = EmployeeImportColumns.CustomField(definition.Key);
            var cell = row.Get(key);
            if (cell is null)
            {
                continue;
            }

            if (definition.IsSensitive && !snapshot.CanViewSensitiveCustomFields)
            {
                continue;
            }

            string? rawValue;
            if (definition.DataType == CustomFieldDataType.Date)
            {
                // Coerce's DateOnly.TryParse expects an invariant/ISO-ish string; native spreadsheet date cells and
                // "dd.MM.yyyy" text need the same parser every other Date column uses before reaching it.
                if (!ImportValueParser.TryParseDate(cell, out var date))
                {
                    errors.Add(ValueError(key, ImportErrorCodes.InvalidDate, cell));
                    continue;
                }

                rawValue = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                var normalized = ImportValueParser.Normalize(cell.Text);
                rawValue = normalized.Length == 0 ? null : normalized;
            }

            var coerced = CustomFieldValueCoercion.Coerce(definition, rawValue);
            if (coerced.IsFailure)
            {
                errors.Add(new ImportRowError(key, coerced.Error.Code, ValueArgs(cell.Text ?? string.Empty)));
                continue;
            }

            values[definition.Id] = coerced.Value;
        }

        return values;
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
