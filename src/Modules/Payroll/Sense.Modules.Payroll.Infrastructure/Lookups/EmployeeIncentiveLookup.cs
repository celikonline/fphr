using Microsoft.EntityFrameworkCore;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Infrastructure.Persistence;

namespace Sense.Modules.Payroll.Infrastructure.Lookups;

/// <summary>Implements Payroll.Contracts.IEmployeeIncentiveLookup so Documents can read the "has an active SGK incentive" signal.</summary>
public sealed class EmployeeIncentiveLookup(PayrollDbContext context) : IEmployeeIncentiveLookup
{
    public async Task<bool> HasActiveIncentiveAsync(Guid tenantId, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var incentiveLawCode = await context.EmployeePayrollProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.EmployeeId == employeeId && p.ValidFrom <= asOf && (p.ValidTo == null || asOf <= p.ValidTo))
            .OrderByDescending(p => p.ValidFrom)
            .Select(p => p.IncentiveLawCode)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrWhiteSpace(incentiveLawCode);
    }
}
