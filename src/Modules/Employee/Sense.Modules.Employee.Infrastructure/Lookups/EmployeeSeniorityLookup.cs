using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// Implements Employee.Contracts.IEmployeeSeniorityLookup so other modules (Payroll's severance/notice estimate,
/// senseik-011 F-06) can read counted seniority days without depending on Employee's Domain/Infrastructure. Reuses
/// Employee.CountedSeniorityDays (the same computation the career/termination screens use, including its
/// no-periods-recorded fallback to the hire date) rather than re-implementing the period math here.
/// </summary>
public sealed class EmployeeSeniorityLookup(EmployeeDbContext context) : IEmployeeSeniorityLookup
{
    public async Task<int> GetCountedSeniorityDaysAsync(
        Guid tenantId, Guid employeeId, EmployeeSeniorityPurpose purpose, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var employee = await context.Employees.AsNoTracking()
            .Include(e => e.EmploymentPeriods)
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .FirstOrDefaultAsync(cancellationToken);

        return employee?.CountedSeniorityDays(Map(purpose), asOf) ?? 0;
    }

    private static SeniorityPurpose Map(EmployeeSeniorityPurpose purpose) => purpose switch
    {
        EmployeeSeniorityPurpose.Leave => SeniorityPurpose.Leave,
        EmployeeSeniorityPurpose.Severance => SeniorityPurpose.Severance,
        EmployeeSeniorityPurpose.JobSecurity => SeniorityPurpose.JobSecurity,
        _ => SeniorityPurpose.Severance,
    };
}
