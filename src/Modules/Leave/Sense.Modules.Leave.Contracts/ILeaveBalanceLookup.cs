namespace Sense.Modules.Leave.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets Payroll estimate the unused-annual-leave payout at exit (11.3.5 eki C2 /
/// İş K. m.59, senseik-011 F-06 "İşten ayrılış sihirbazı") without depending on Leave's Domain/Application. No
/// "remaining balance" port existed before this (only annual usage - <see cref="ILeaveUsageReader"/> - and monthly
/// timesheet reads); this wraps <c>LeaveBalanceService.ProjectAsync</c>, the same projection the "İzin Özeti" screen
/// (<c>GetEmployeeLeaveBalancesQuery</c>) uses, summed across every Annual-coded (<c>LeaveTypeCode.Annual</c>) leave
/// type of the employee's rule set (a tenant can have more than one, mirroring <see cref="ILeaveUsageReader"/>'s
/// own note). Implemented by Sense.Modules.Leave.Infrastructure and registered by LeaveServiceCollectionExtensions.
/// </summary>
public interface ILeaveBalanceLookup
{
    /// <summary>
    /// The employee's current Annual-type balance ("kalan bakiye", <c>LeaveBalance.CurrentBalance</c> summed across
    /// every Annual-coded type), or null when the employee has no leave profile yet.
    /// </summary>
    Task<decimal?> GetRemainingAnnualLeaveDaysAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}
