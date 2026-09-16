using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeSalaryLookup so other modules (Payroll, 11.3.07) can read the
/// employee's current salary pay frequency without depending on Employee's Domain or Infrastructure.</summary>
public sealed class EmployeeSalaryLookup(EmployeeDbContext context) : IEmployeeSalaryLookup
{
    public async Task<EmployeePayPeriod?> GetCurrentPayPeriodAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await context.Set<SalaryRecord>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.EffectiveFrom <= today)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new { r.PayFrequency, r.EffectiveFrom })
            .FirstOrDefaultAsync(cancellationToken);

        return current is null ? null : new EmployeePayPeriod(Map(current.PayFrequency), current.EffectiveFrom);
    }

    public async Task<EmployeeCurrentSalary?> GetCurrentSalaryAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await context.Set<SalaryRecord>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.EffectiveFrom <= today)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        return current is null
            ? null
            : new EmployeeCurrentSalary(current.Amount, current.Currency, MapSalaryType(current.SalaryType), Map(current.PayFrequency), current.EffectiveFrom);
    }

    private static EmployeeSalaryType MapSalaryType(SalaryType salaryType) => salaryType switch
    {
        SalaryType.Net => EmployeeSalaryType.Net,
        SalaryType.Gross => EmployeeSalaryType.Gross,
        _ => EmployeeSalaryType.Gross,
    };

    private static EmployeePayFrequency Map(PayFrequency payFrequency) => payFrequency switch
    {
        PayFrequency.Monthly => EmployeePayFrequency.Monthly,
        PayFrequency.Daily => EmployeePayFrequency.Daily,
        PayFrequency.Yearly => EmployeePayFrequency.Yearly,
        PayFrequency.Weekly => EmployeePayFrequency.Weekly,
        PayFrequency.Hourly => EmployeePayFrequency.Hourly,
        _ => EmployeePayFrequency.Monthly,
    };
}
