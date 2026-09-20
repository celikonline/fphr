using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Infrastructure.Persistence;
using DomainDisabilityDegree = Sense.Modules.Employee.Domain.Employees.DisabilityDegree;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// Implements Employee.Contracts.IEmployeeDisabilityLookup: maps the Domain DisabilityDegree (null or
/// <see cref="DomainDisabilityDegree.None"/> both fold into <see cref="EmployeeDisabilityDegree.None"/>) to the
/// Contracts-only mirror enum senseik-019 2b (Payroll's GVK md.31 matrah indirimi) needs.
/// </summary>
public sealed class EmployeeDisabilityLookup(EmployeeDbContext context) : IEmployeeDisabilityLookup
{
    public async Task<EmployeeDisabilityDegree?> GetDegreeAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var row = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => new { e.DisabilityDegree })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return row.DisabilityDegree switch
        {
            DomainDisabilityDegree.First => EmployeeDisabilityDegree.First,
            DomainDisabilityDegree.Second => EmployeeDisabilityDegree.Second,
            DomainDisabilityDegree.Third => EmployeeDisabilityDegree.Third,
            _ => EmployeeDisabilityDegree.None,
        };
    }
}
