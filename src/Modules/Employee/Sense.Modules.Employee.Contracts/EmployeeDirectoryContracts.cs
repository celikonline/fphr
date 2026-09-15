namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee grid filter; also the "select all matching" part of an <see cref="EmployeeSelection"/>.</summary>
public sealed record EmployeeFilter(string? Q, Guid? OrgUnitId, bool IncludeSubUnits, string? Status, bool? HasUserAccount, Guid? ManagerEmployeeId);

/// <summary>
/// Grid selection. "Tümünü seç": Filter set + EmployeeIds null => every employee matching the filter (within the caller's
/// data scope) minus ExcludedEmployeeIds. EmployeeIds set => exactly those (still scope-filtered), minus ExcludedEmployeeIds.
/// </summary>
public sealed record EmployeeSelection(IReadOnlyList<Guid>? EmployeeIds, EmployeeFilter? Filter, IReadOnlyList<Guid>? ExcludedEmployeeIds);

/// <summary>Contact data other modules need to reach an employee (e.g. invitations).</summary>
public sealed record EmployeeContact(Guid EmployeeId, string FirstName, string LastName, string? WorkEmail, string? PersonalEmail, Guid? UserId, string Status);

/// <summary>Cross-module employee reads over selections/units.</summary>
public interface IEmployeeDirectory
{
    /// <summary>Resolves a grid selection to employee ids, applying the caller's data scope for employee.employees.read.</summary>
    Task<IReadOnlyList<Guid>> ResolveSelectionAsync(Guid tenantId, EmployeeSelection selection, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeContact>> GetContactsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>Active (not Inactive) employees assigned directly to one of the given units (callers expand subtrees themselves).</summary>
    Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsInOrgUnitsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken ct = default);
}

/// <summary>
/// Contact reads only (no data-scope dependency), so hosts without the caller/permission pipeline - e.g. Sense.Worker
/// resolving notification recipients - can resolve it with just EmployeeDbContext. Same data as
/// <see cref="IEmployeeDirectory.GetContactsAsync"/>.
/// </summary>
public interface IEmployeeContactLookup
{
    Task<IReadOnlyList<EmployeeContact>> GetContactsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
}

/// <summary>Links an Identity user to an employee record; idempotent and persisted immediately.</summary>
public interface IEmployeeUserLinker
{
    Task LinkUserAsync(Guid tenantId, Guid employeeId, Guid userId, CancellationToken ct = default);
}

/// <summary>Profile fields beyond <see cref="EmployeeContact"/> (name/email) that a report or card needs. Missing/unset fields are null.</summary>
public sealed record EmployeeProfileDetail(Guid EmployeeId, DateOnly? HireDate, string? WorkPhone);

/// <summary>
/// Profile reads only (no data-scope dependency), same shape as <see cref="IEmployeeContactLookup"/> so hosts without
/// the caller/permission pipeline (e.g. Sense.Worker) can resolve it with just EmployeeDbContext.
/// </summary>
public interface IEmployeeProfileLookup
{
    Task<IReadOnlyList<EmployeeProfileDetail>> GetProfileDetailsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
}
