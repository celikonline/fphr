namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee grid filter; also the "select all matching" part of an <see cref="EmployeeSelection"/>.</summary>
/// <summary>
/// Employee grid filters. <paramref name="EmploymentType"/>/<paramref name="ContractType"/> are the camelCase enum
/// names the API serializes; <paramref name="MissingInfo"/> is the "Eksik bilgi" switch (see EmployeeCriteria).
/// <paramref name="OrgUnitIds"/> is a multi-unit filter, unioned with <paramref name="OrgUnitId"/> (both expand to
/// descendants when <paramref name="IncludeSubUnits"/>); null/empty = no unit restriction. Terminated employees are left
/// out unless <paramref name="IncludeTerminated"/> is set or <paramref name="Status"/> is given explicitly.
/// </summary>
public sealed record EmployeeFilter(
    string? Q,
    Guid? OrgUnitId,
    bool IncludeSubUnits,
    string? Status,
    bool? HasUserAccount,
    Guid? ManagerEmployeeId,
    string? EmploymentType = null,
    string? ContractType = null,
    bool? MissingInfo = null,
    IReadOnlyList<Guid>? OrgUnitIds = null,
    bool IncludeTerminated = false);

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

    /// <summary>Every <c>EmployeeStatus.Active</c> employee id for the tenant, no org-unit/permission-scope filtering
    /// (system-level enumeration for the Payroll period run, 11.3.07 P3 - Draft/Suspended/Terminated employees are
    /// excluded, unlike <see cref="ListActiveEmployeeIdsInOrgUnitsAsync"/>'s broader "not Terminated").</summary>
    Task<IReadOnlyList<Guid>> ListActiveEmployeeIdsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Employees eligible for a payroll period covering <paramref name="periodStart"/>..<paramref name="periodEnd"/>:
    /// <c>Active</c> employees hired on/before the period end, plus <c>Terminated</c> employees whose termination date falls
    /// on/after the period start (their exit-month payslip). No org-unit/permission-scope filtering.</summary>
    Task<IReadOnlyList<Guid>> ListPayrollCandidateEmployeeIdsAsync(Guid tenantId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
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
