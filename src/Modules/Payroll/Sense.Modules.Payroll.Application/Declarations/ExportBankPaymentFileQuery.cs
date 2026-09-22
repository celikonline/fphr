using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>
/// The bank payment list (EFT/havale) of an approved, paid or locked period as a generic CSV. It carries employees' IBANs, so it
/// needs its own permission, refuses to build with any missing account (a partial list would look complete) and only ever leaves
/// the server when the file total equals the payroll net total. Who downloaded which period is logged (never the IBANs).
/// </summary>
[RequiresPermission("payroll.bankfile.export")]
public sealed record ExportBankPaymentFileQuery(Guid PeriodId) : IQuery<ExportFileResult>;

public sealed class ExportBankPaymentFileHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IEmployeeDirectory employeeDirectory,
    IEmployeePayoutAccountLookup accounts,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ILogger<ExportBankPaymentFileHandler> logger) : IQueryHandler<ExportBankPaymentFileQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportBankPaymentFileQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
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
            logger.LogWarning(
                "Bank file refused for payroll period {Year}-{Month}: {Missing} of {Total} employees have no primary account.",
                period.Year, period.Month, ids.Count(id => !byEmployee.ContainsKey(id)), ids.Count);
            return Error.Rule(PayrollDomainErrors.BankFileMissingAccounts);
        }

        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, ids, cancellationToken)).ToDictionary(c => c.EmployeeId);
        var payments = due
            .Select(p =>
            {
                var contact = contacts.GetValueOrDefault(p.EmployeeId);
                var name = contact is null ? p.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
                var account = byEmployee[p.EmployeeId];
                return new BankPayment(name, account.Iban, p.NetAmount, account.BankName);
            })
            .OrderBy(p => p.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var netTotal = decimal.Round(due.Sum(p => p.NetAmount), 2, MidpointRounding.AwayFromZero);
        if (BankPaymentFileBuilder.Total(payments) != netTotal)
        {
            return Error.Rule(PayrollDomainErrors.BankFileTotalMismatch);
        }

        logger.LogInformation(
            "Bank file built for payroll period {Year}-{Month} by user {UserId}: {Count} payments, total {Total}.",
            period.Year, period.Month, currentUser.UserId, payments.Count, netTotal);

        var csv = BankPaymentFileBuilder.Build(payments, $"Maas {period.Year}-{period.Month:D2}");
        return new ExportFileResult($"banka-odeme_{period.Year}-{period.Month:D2}.csv", "text/csv; charset=utf-8", BankPaymentFileBuilder.Encode(csv));
    }
}
