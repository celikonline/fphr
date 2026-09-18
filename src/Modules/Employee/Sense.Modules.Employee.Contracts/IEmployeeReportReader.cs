namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// One employee as report providers need it (Kolay İK "Çalışan listesi raporu" columns + the identity/placement
/// columns every other report repeats: Ad, Soyad, kariyer bilgileri, İşe giriş tarihi, Çalışma durumu - 11.3.12).
/// Enum-typed fields travel as the C# enum name (e.g. "Active", "FullTime") so consumers localize them without
/// depending on Employee's Domain. <see cref="OrgAssignments"/> is the current career record's per-level placement
/// (OrgUnitTypeId → OrgUnitId); <see cref="OrgUnitId"/> is the denormalized deepest unit.
/// </summary>
public sealed record EmployeeReportRow(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? NationalId,
    DateOnly? HireDate,
    DateOnly? TerminationDate,
    string Status,
    Guid? OrgUnitId,
    Guid? ManagerEmployeeId,
    string? WorkEmail,
    string? MobilePhone,
    string? EmploymentType,
    string? ContractType,
    string Gender,
    DateOnly? BirthDate,
    IReadOnlyDictionary<Guid, Guid> OrgAssignments);

/// <summary>
/// Read-only cross-module port for report providers (Reporting catalog, roadmap #70). Applies **no** permission or data
/// scope: the caller resolves the visible employee set (IDataScopeService) and passes it as <c>employeeIds</c>; null
/// means every employee of the tenant. Implemented by Sense.Modules.Employee.Infrastructure and registered by
/// EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeReportReader
{
    /// <param name="employeeIds">Visible set; null = no restriction.</param>
    /// <param name="orgUnitIds">Already expanded (self + descendants) unit filter on the denormalized unit; null/empty = no filter.</param>
    /// <param name="includeTerminated">False drops <c>Terminated</c> employees (Draft/Suspended stay, they are still on the payroll list).</param>
    Task<IReadOnlyList<EmployeeReportRow>> ListAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid>? employeeIds,
        IReadOnlyCollection<Guid>? orgUnitIds,
        bool includeTerminated,
        CancellationToken cancellationToken = default);
}
