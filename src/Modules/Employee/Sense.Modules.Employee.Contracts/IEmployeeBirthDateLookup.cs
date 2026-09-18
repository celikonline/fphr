namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets Leave apply the ≤18/≥50 statutory age exception (4857 md.53/IV) to
/// <c>EmployeeLeaveProfile.BirthDate</c> without depending on Employee's Domain or Infrastructure. BirthDate is
/// sensitive (KVKK) so it is never carried on an integration event payload (mirrors IEmployeeSalaryLookup for
/// EmployeeCurrentSalary) - this Contracts-port read is the only way another module may see it, and even then only
/// the raw date, never bundled with other sensitive fields.
/// Implemented by Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule (and explicitly
/// in Sense.Worker, mirroring IEmployeeSalaryLookup, since Leave's EmployeeHiredHandler and
/// LeaveBalanceRecalculationJob consumers run there).
/// </summary>
public interface IEmployeeBirthDateLookup
{
    /// <summary>The employee's birth date, or null if not recorded.</summary>
    Task<DateOnly?> GetBirthDateAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched form of <see cref="GetBirthDateAsync"/> for callers resolving many employees at once
    /// (LeaveBalanceRecalculationJob's nightly tenant-wide pass) - one round trip instead of one per employee.
    /// Employees with no recorded birth date are simply absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetBirthDatesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
