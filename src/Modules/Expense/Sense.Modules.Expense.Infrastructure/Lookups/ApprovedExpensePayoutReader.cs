using Microsoft.EntityFrameworkCore;
using Sense.Modules.Expense.Contracts;
using Sense.Modules.Expense.Domain.Advances;
using Sense.Modules.Expense.Domain.ExpenseClaims;
using Sense.Modules.Expense.Infrastructure.Persistence;

namespace Sense.Modules.Expense.Infrastructure.Lookups;

/// <summary>Implements Expense.Contracts.IApprovedExpensePayoutReader (Payroll bridge, S-13).</summary>
public sealed class ApprovedExpensePayoutReader(ExpenseDbContext context) : IApprovedExpensePayoutReader
{
    public async Task<IReadOnlyList<ExpensePayoutSummary>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var ids = employeeIds.ToList();

        var approvedTotals = await context.ExpenseClaims.AsNoTracking()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.EmployeeId)
                && c.Status == ExpenseClaimStatus.Approved
                && c.DecidedAt != null && c.DecidedAt.Value.Year == year && c.DecidedAt.Value.Month == month)
            .GroupBy(c => c.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Total = g.Sum(c => c.TotalAmount) })
            .ToListAsync(cancellationToken);

        var openAdvances = await context.AdvanceRequests.AsNoTracking()
            .Where(a => a.TenantId == tenantId && ids.Contains(a.EmployeeId) && a.Status == AdvanceRequestStatus.Paid)
            .GroupBy(a => a.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Total = g.Sum(a => a.Amount) })
            .ToListAsync(cancellationToken);

        var employeesWithData = approvedTotals.Select(x => x.EmployeeId)
            .Union(openAdvances.Select(x => x.EmployeeId));

        return employeesWithData
            .Select(employeeId => new ExpensePayoutSummary(
                employeeId,
                approvedTotals.FirstOrDefault(x => x.EmployeeId == employeeId)?.Total ?? 0m,
                openAdvances.FirstOrDefault(x => x.EmployeeId == employeeId)?.Total ?? 0m))
            .ToList();
    }
}
