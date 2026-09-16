using Sense.Modules.Expense.Contracts;
using Sense.Modules.Payroll.Application.Ports;

namespace Sense.Modules.Payroll.Infrastructure.Adapters;

/// <summary>
/// Adapts Expense.Contracts.IApprovedExpensePayoutReader (S-13, TB-7 - faz-07 tur 1 sözleşmesi) to Payroll's
/// IExpensePayoutReader port. Mirrors <see cref="AttendanceWorkedTimeReaderAdapter"/>'s role (adapt one cross-module
/// Contracts reader, batch-shaped, to this module's own narrow per-employee port).
/// </summary>
public sealed class ExpensePayoutReaderAdapter(IApprovedExpensePayoutReader payouts) : IExpensePayoutReader
{
    public async Task<ExpensePayoutFigures?> GetAsync(Guid tenantId, Guid employeeId, int year, int month, CancellationToken cancellationToken = default)
    {
        // The Expense port is batch-shaped (Payroll's own period run calls this per employee in a loop today -
        // see PayrollPeriodCalculationService.CalculateOneAsync - so this always asks for a one-employee batch).
        // Unlike Attendance's port, absence from the result is a valid "zero" (no approved expense/open advance this
        // month is the common case), not a "veri yok" distinction - see IApprovedExpensePayoutReader's own doc.
        var results = await payouts.GetAsync(tenantId, year, month, [employeeId], cancellationToken);
        var match = results.FirstOrDefault(r => r.EmployeeId == employeeId);
        return match is null ? null : new ExpensePayoutFigures(match.ApprovedExpenseTotal, match.OpenAdvanceBalance);
    }
}
