namespace Sense.Modules.Employee.Contracts;

/// <summary>Disability degree mirroring Employee.Domain.Employees.DisabilityDegree (GVK md.31 gelir vergisi matrah
/// indirimi, senseik-019 2b), exposed as a Contracts-only shape so Payroll can read it without depending on
/// Employee's Domain/Infrastructure. <see cref="None"/> covers both an explicit "engeli yok" and "not recorded" -
/// Payroll treats both the same way (no reduction).</summary>
public enum EmployeeDisabilityDegree
{
    None = 0,
    First = 1,
    Second = 2,
    Third = 3,
}

/// <summary>
/// Read-only cross-module lookup consumed by Payroll (11.3.07-bordro.md GVK md.31 engelli çalışan gelir vergisi
/// matrah indirimi, senseik-019 2b). Implemented by Sense.Modules.Employee.Infrastructure and registered by
/// EmployeeModule.AddModule. Mirrors IEmployeeDocumentEligibilityLookup's "derived value only" shape but exposes the
/// actual degree (not just a boolean) since Payroll's indirim tutarı dereceye göre değişir.
/// </summary>
public interface IEmployeeDisabilityLookup
{
    /// <summary>Null if the employee does not exist for this tenant; <see cref="EmployeeDisabilityDegree.None"/> if
    /// the employee exists but has no disability recorded (explicit "yok" or never entered).</summary>
    Task<EmployeeDisabilityDegree?> GetDegreeAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}
