using Microsoft.EntityFrameworkCore;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Infrastructure.Persistence;

namespace Sense.Modules.Payroll.Infrastructure.Lookups;

/// <summary>Implements Payroll.Contracts.IEmployeeIncentiveLookup so Documents can read the "has an active SGK incentive" signal: the profile has an incentive law code, or an incentive assignment covers the month.</summary>
public sealed class EmployeeIncentiveLookup(PayrollDbContext context) : IEmployeeIncentiveLookup
{
    public async Task<bool> HasActiveIncentiveAsync(Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var incentiveLawCode = await context.EmployeePayrollProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.EmployeeId == employeeId && p.ValidFrom <= asOf && (p.ValidTo == null || asOf <= p.ValidTo))
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => p.IncentiveLawCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(incentiveLawCode))
        {
            return true;
        }

        // PAY-INCENTIVE: an assignment covering the month of asOf whose definition is usable that month also counts.
        var monthStart = EmployeeIncentiveAssignment.FirstOfMonth(asOf);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return await (
            from assignment in context.IncentiveAssignments.AsNoTracking()
            join definition in context.IncentiveDefinitions.AsNoTracking() on assignment.IncentiveDefinitionId equals definition.Id
            where assignment.TenantId == tenantId && assignment.EmployeeId == employeeId
                && assignment.ValidFromMonth <= monthStart && (assignment.ValidToMonth == null || assignment.ValidToMonth >= monthStart)
                && definition.IsActive && definition.ValidFrom <= monthEnd && (definition.ValidTo == null || definition.ValidTo >= monthStart)
            select assignment.Id).AnyAsync(cancellationToken);
    }
}
