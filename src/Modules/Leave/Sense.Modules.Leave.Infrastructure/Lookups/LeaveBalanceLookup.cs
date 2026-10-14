using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;

namespace Sense.Modules.Leave.Infrastructure.Lookups;

/// <summary>
/// Implements Leave.Contracts.ILeaveBalanceLookup (Payroll's severance/notice estimate, senseik-011 F-06) by
/// wrapping the same <see cref="LeaveBalanceService.ProjectAsync"/> the "İzin Özeti" screen uses
/// (<c>GetEmployeeLeaveBalancesQuery</c>), summed across every Annual-coded type of the employee's rule set.
/// <paramref name="tenantId"/> is accepted for parity with the module's other Contracts lookups (IEmployeeSalaryLookup-
/// style explicit scoping) but not filtered on again here: the repositories below are already tenant-scoped by the
/// ambient ITenantContext (ModuleDbContext's global query filter), and this port has no Worker/cross-tenant caller.
/// </summary>
public sealed class LeaveBalanceLookup(
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    LeaveBalanceService balances) : ILeaveBalanceLookup
{
    public async Task<decimal?> GetRemainingAnnualLeaveDaysAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var profile = await profiles.FindByEmployeeIdAsync(employeeId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var ruleSet = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
        if (ruleSet.IsFailure)
        {
            return null;
        }

        var annualTypeIds = ruleSet.Value.Types.Where(t => t.Code == LeaveTypeCode.Annual).Select(t => t.Id).ToHashSet();
        if (annualTypeIds.Count == 0)
        {
            return 0m;
        }

        var projected = await balances.ProjectAsync(profile, ruleSet.Value, cancellationToken);
        return projected.Where(b => annualTypeIds.Contains(b.LeaveTypeId)).Sum(b => b.CurrentBalance);
    }

    public async Task<decimal?> GetRemainingAnnualLeaveDaysAsOfAsync(
        Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var profile = await profiles.FindByEmployeeIdAsync(employeeId, cancellationToken);
        if (profile is null || profile.TenantId != tenantId) return null;
        var ruleSet = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
        if (ruleSet.IsFailure || ruleSet.Value.TenantId != tenantId) return null;
        var annualTypeIds = ruleSet.Value.Types.Where(t => t.Code == LeaveTypeCode.Annual).Select(t => t.Id).ToHashSet();
        if (annualTypeIds.Count == 0) return 0m;
        var projected = await balances.ProjectAsOfAsync(profile, ruleSet.Value, asOf, cancellationToken);
        return projected.Where(b => annualTypeIds.Contains(b.LeaveTypeId)).Sum(b => b.CurrentBalance);
    }
}
