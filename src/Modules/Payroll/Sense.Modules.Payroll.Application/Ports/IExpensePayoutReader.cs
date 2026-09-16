namespace Sense.Modules.Payroll.Application.Ports;

/// <summary>
/// One employee's Expense-side payout figures for a payroll period's calendar month (TB-7 "Expense köprüsü", faz-07
/// tur 1 madde 3). <see cref="ApprovedExpenseTotal"/> is the only figure <see cref="Domain.Calculation.PayrollCalculator"/>
/// currently consumes (feeds <see cref="Domain.Calculation.PayrollCalculationInput.ExpenseReimbursement"/> as a
/// tax-free NET addition); <see cref="OpenAdvanceBalance"/> is informational this slice, reserved for a future
/// avans-deduction bridge (not wired into the calculator yet). <see cref="UndocumentedExpenseTotal"/> (S-13 eki, PO
/// kararı) is the receipt-less (fişsiz) counterpart of <see cref="ApprovedExpenseTotal"/> - never fed into the
/// calculator, only surfaced for the Excel export's "Fişsiz masraf (bordroya eklenmedi)" column.
/// </summary>
public sealed record ExpensePayoutFigures(decimal ApprovedExpenseTotal, decimal OpenAdvanceBalance, decimal UndocumentedExpenseTotal = 0m);

/// <summary>
/// Payroll's read port for the employee's monthly approved-expense-reimbursement figures - mirrors
/// <see cref="IWorkedTimeReader"/>'s shape and null-fallback contract (adapt an Expense.Contracts reader in
/// Infrastructure, register in the module's ServiceCollectionExtensions).
/// <c>ExpensePayoutReaderAdapter</c> (Payroll.Infrastructure/Adapters) adapts
/// Expense.Contracts.IApprovedExpensePayoutReader (S-13, TB-7); a null/absent result means "no approved expense
/// reimbursement or open advance this month", which the calculation engine treats as 0 (today's behaviour if the
/// port is never wired).
/// </summary>
public interface IExpensePayoutReader
{
    /// <summary>Null = no data for this employee/period (Expense not tracking it, or no adapter wired) - the caller
    /// treats this exactly like a zero reimbursement, i.e. today's behaviour before this port existed.</summary>
    Task<ExpensePayoutFigures?> GetAsync(Guid tenantId, Guid employeeId, int year, int month, CancellationToken cancellationToken = default);
}
