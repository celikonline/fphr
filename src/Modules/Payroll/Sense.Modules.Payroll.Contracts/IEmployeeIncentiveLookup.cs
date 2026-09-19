namespace Sense.Modules.Payroll.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets Documents materialize the IncentiveDocument conditional requirement
/// (DocumentRequirementCondition.HasIncentive) without depending on Payroll's Domain or Infrastructure, mirroring
/// Assets.Contracts.IAssetLookup's style. Implemented by Sense.Modules.Payroll.Infrastructure and registered by
/// PayrollModule.AddModule (senseik-010/F-03: EmployeePayrollProfile.IncentiveLawCode already existed but was never
/// read by anything - see docs/handoffs/employee-sicil-uyruk.md §3).
/// </summary>
public interface IEmployeeIncentiveLookup
{
    /// <summary>
    /// True if the employee's payroll profile in effect on <paramref name="asOf"/> has a non-empty IncentiveLawCode.
    /// False when the employee has no profile at all (e.g. not hired through Payroll yet).
    /// </summary>
    Task<bool> HasActiveIncentiveAsync(Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default);
}
