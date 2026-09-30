namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee gender mirroring Employee.Domain.Employees.Gender as a Contracts-only shape (Leave's gender-restricted types).</summary>
public enum EmployeeGender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
    Other = 3,
}

/// <summary>
/// Read-only cross-module lookup: lets other modules (e.g. Organization, when assigning
/// OrgUnit.ManagerEmployeeId) validate an employee reference without depending on Employee's Domain
/// or Infrastructure. Implemented by Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeLookup
{
    /// <summary>True if an active (not Inactive) employee with this id exists for the given tenant.</summary>
    Task<bool> ExistsActiveAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>True if any active (not Inactive) employee is assigned to one of the given org units.</summary>
    Task<bool> AnyActiveInOrgUnitsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken cancellationToken = default);

    /// <summary>Active (not terminated) employees per org unit; employees without a unit are not counted.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountActiveByOrgUnitAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The employee's org unit, or null if the employee does not exist or has no unit.</summary>
    Task<Guid?> GetOrgUnitIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The employee's direct manager, or null if the employee does not exist or has no manager assigned.</summary>
    Task<Guid?> GetManagerEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The single active employee whose national id matches exactly (import row matching, e.g. Attendance's PDKS
    /// template); null when no active employee has this national id. National id is stored unmasked for this reason.
    /// </summary>
    Task<Guid?> FindActiveByNationalIdAsync(Guid tenantId, string nationalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The single active employee whose sicil numarası matches exactly (senseik-009/F-02 dual-key transition: import
    /// row matching prefers this over <see cref="FindActiveByNationalIdAsync"/> when both a sicil numarası column and
    /// a match are available); null when no active employee has this sicil numarası.
    /// </summary>
    Task<Guid?> FindActiveBySicilNumarasiAsync(Guid tenantId, string sicilNumarasi, CancellationToken cancellationToken = default);

    /// <summary>
    /// The employee's SGK "meslek kodu" (senseik-010/F-03), for Payroll's APHB/e-Bildirge export preparation (the
    /// export itself is out of scope; this only surfaces the stored value, mirroring
    /// Payroll.Contracts.IEmployeeIncentiveLookup's doc comment). Null when the employee does not exist or has no
    /// occupation code set.
    /// </summary>
    Task<string?> GetSgkOccupationCodeAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The employee's recorded gender (Leave's cinsiyete bağlı izin türleri: Babalık/Doğum/Süt); null when the employee does
    /// not exist for the tenant, <see cref="EmployeeGender.Unspecified"/> when none is recorded.
    /// </summary>
    Task<EmployeeGender?> GetGenderAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The employee of any status whose sicil numarası matches exactly (sicil is tenant-unique across statuses), or null. Unlike
    /// <see cref="FindActiveBySicilNumarasiAsync"/> it tells an unknown sicil apart from a terminated employee - e.g. Attendance's
    /// terminal credential import reports "employee not active" rather than "not found" for someone who has left.
    /// </summary>
    Task<EmployeeSicilMatch?> FindBySicilNumarasiAsync(Guid tenantId, string sicilNumarasi, CancellationToken cancellationToken = default);

    /// <summary>
    /// FG-007 "Ekibim": the manager's direct reports (<c>ManagerEmployeeId == managerEmployeeId</c>, not terminated), ordered by
    /// display name. Direct reports only - unlike the Team data scope (Shared.Contracts.Security.IEmployeeScopeLookup), no
    /// indirect chain. Empty when the employee manages nobody (or does not exist for the tenant).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(Guid tenantId, Guid managerEmployeeId, CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="IEmployeeLookup.FindBySicilNumarasiAsync"/>: the employee and whether they are terminated (işten ayrılmış).</summary>
public sealed record EmployeeSicilMatch(Guid EmployeeId, bool IsTerminated);
