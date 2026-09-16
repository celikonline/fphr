namespace Sense.Modules.Expense.Contracts;

/// <summary>
/// One employee's Expense-side payout snapshot for a given calendar month (Payroll bridge, S-13, faz-07 tur 1 PO
/// kararı ile onaylı sözleşme). <see cref="ApprovedExpenseTotal"/> is the sum of the *receipted* items (ItemAmount
/// where <c>ReceiptFileId</c> is set) across claims whose approval decision (<c>DecidedAt</c>) falls within the given
/// year/month and that are not yet <c>Paid</c> (i.e. Status == Approved) - this is the tax-free reimbursement Payroll
/// adds to that month's payslip. <see cref="OpenAdvanceBalance"/> is the running total of <c>AdvanceRequest</c>
/// amounts already paid out (Status == Paid) to the employee that Expense has no repayment/deduction record for yet -
/// it is a snapshot as of "now", not scoped to the requested month (Expense has no repayment tracking this slice);
/// Payroll does not consume it yet (kept for a later deduction bridge), see faz-07 tur 1 madde 3.
/// <see cref="UndocumentedExpenseTotal"/> (S-13 eki, PO kararı G-7-09/tur-2 madde 5) is the sum of the same claims'
/// *unreceipted* items (<c>ReceiptFileId</c> null) - Payroll never adds this to NET (a fişsiz kalem is not a tax-free
/// reimbursement), it is reported separately (Excel-only, informational). Appended at the end (default 0m) so
/// existing positional callers are unaffected.
/// </summary>
public sealed record ExpensePayoutSummary(Guid EmployeeId, decimal ApprovedExpenseTotal, decimal OpenAdvanceBalance, decimal UndocumentedExpenseTotal = 0m);

/// <summary>
/// Cross-module read port (Payroll consumes this instead of reaching into Expense's tables directly). An employee
/// absent from the result had neither an approved-this-month expense claim nor an open advance balance - Payroll
/// treats missing entries as zero (unlike Attendance's <c>IAttendanceMonthlySummaryReader</c>, there is no "veri yok"
/// distinction here, zero is a valid and common result).
/// </summary>
public interface IApprovedExpensePayoutReader
{
    Task<IReadOnlyList<ExpensePayoutSummary>> GetAsync(
        Guid tenantId, int year, int month, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
