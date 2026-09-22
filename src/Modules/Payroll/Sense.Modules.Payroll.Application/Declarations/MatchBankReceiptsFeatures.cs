using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>One employee named in a <see cref="BankReceiptMatchSummary"/> bucket.</summary>
public sealed record BankReceiptSummaryLine(Guid EmployeeId, string EmployeeName, decimal Amount);

/// <summary>An employee whose receipt amount differs from what payroll expected.</summary>
public sealed record BankReceiptMismatchLine(Guid EmployeeId, string EmployeeName, decimal ExpectedAmount, decimal ReceiptAmount);

/// <summary>
/// Dekont eşleştirme sonucu: confirmed payments are marked on their payslip (<see cref="Domain.Payslips.Payslip.PaymentConfirmedAt"/>);
/// the other three lists are reported but change nothing, so a bookkeeper can re-upload a corrected file.
/// </summary>
public sealed record BankReceiptMatchSummary(
    int ConfirmedCount,
    IReadOnlyList<BankReceiptMismatchLine> AmountMismatches,
    IReadOnlyList<BankReceiptSummaryLine> Rejected,
    IReadOnlyList<BankReceiptSummaryLine> Unmatched,
    int UnrecognizedRowCount);

/// <summary>
/// Matches a bank's return file (generic CSV: IBAN, amount, optional status - any bank's export can be turned into this) against the
/// period's expected payments and marks each confirmed payslip's <see cref="Domain.Payslips.Payslip.PaymentConfirmedAt"/>. Same
/// permission and period-status gate as <see cref="ExportBankPaymentFileQuery"/>, since it reads the same IBANs.
/// </summary>
[RequiresPermission("payroll.bankfile.export")]
public sealed record MatchBankReceiptsCommand(Guid PeriodId, string CsvContent) : ICommand<BankReceiptMatchSummary>;

public sealed class MatchBankReceiptsValidator : AbstractValidator<MatchBankReceiptsCommand>
{
    public MatchBankReceiptsValidator()
    {
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.CsvContent).NotEmpty();
    }
}

public sealed class MatchBankReceiptsHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IEmployeeDirectory employeeDirectory,
    IEmployeePayoutAccountLookup accounts,
    ITenantContext tenant,
    TimeProvider clock,
    ILogger<MatchBankReceiptsHandler> logger) : ICommandHandler<MatchBankReceiptsCommand, BankReceiptMatchSummary>
{
    public async Task<Result<BankReceiptMatchSummary>> Handle(MatchBankReceiptsCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status == PayrollPeriodStatus.Open)
        {
            return Error.Rule(PayrollDomainErrors.BankFilePeriodNotApproved);
        }

        var due = (await payslips.ListByPeriodAsync(period.Id, cancellationToken)).Where(p => p.NetAmount > 0m).ToList();
        if (due.Count == 0)
        {
            return Error.Rule(PayrollDomainErrors.BankFileNoPayments);
        }

        var ids = due.Select(p => p.EmployeeId).ToList();
        var byEmployee = (await accounts.GetPrimaryPayoutAccountsAsync(tenant.TenantId, ids, cancellationToken)).ToDictionary(a => a.EmployeeId);
        if (ids.Any(id => !byEmployee.ContainsKey(id)))
        {
            return Error.Rule(PayrollDomainErrors.BankFileMissingAccounts);
        }

        var rows = BankReceiptMatcher.ParseCsv(command.CsvContent);
        if (rows.Count == 0)
        {
            return Error.Validation(PayrollDomainErrors.ReceiptCsvEmpty);
        }

        var expected = due.Select(p => new ExpectedPayment(p.EmployeeId, byEmployee[p.EmployeeId].Iban, p.NetAmount)).ToList();
        var result = BankReceiptMatcher.Match(expected, rows);

        var now = clock.GetUtcNow();
        var payslipsByEmployee = due.ToDictionary(p => p.EmployeeId);
        foreach (var match in result.Confirmed)
        {
            payslipsByEmployee[match.EmployeeId].ConfirmPayment(now);
        }

        var namedIds = result.AmountMismatches.Select(m => m.EmployeeId)
            .Concat(result.Rejected.Select(r => r.EmployeeId))
            .Concat(result.UnmatchedEmployeeIds)
            .Distinct()
            .ToList();
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, namedIds, cancellationToken)).ToDictionary(c => c.EmployeeId);
        string Name(Guid employeeId)
        {
            var contact = contacts.GetValueOrDefault(employeeId);
            return contact is null ? employeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
        }

        logger.LogInformation(
            "Bank receipts matched for payroll period {Year}-{Month}: {Confirmed} confirmed, {Mismatches} mismatched, {Rejected} rejected, {Unmatched} unmatched, {Unrecognized} unrecognized rows.",
            period.Year, period.Month, result.Confirmed.Count, result.AmountMismatches.Count, result.Rejected.Count, result.UnmatchedEmployeeIds.Count, result.UnrecognizedRowCount);

        return new BankReceiptMatchSummary(
            result.Confirmed.Count,
            result.AmountMismatches.Select(m => new BankReceiptMismatchLine(m.EmployeeId, Name(m.EmployeeId), m.ExpectedAmount, m.ReceiptAmount)).ToList(),
            result.Rejected.Select(r => new BankReceiptSummaryLine(r.EmployeeId, Name(r.EmployeeId), r.Amount)).ToList(),
            result.UnmatchedEmployeeIds.Select(id => new BankReceiptSummaryLine(id, Name(id), payslipsByEmployee[id].NetAmount)).ToList(),
            result.UnrecognizedRowCount);
    }
}
