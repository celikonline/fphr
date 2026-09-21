namespace Sense.Modules.Employee.Contracts;

/// <summary>An employee's primary bank account as a payment file needs it: the plain IBAN and the bank's name.</summary>
public sealed record EmployeePayoutAccount(Guid EmployeeId, string Iban, string? BankName);

/// <summary>
/// Cross-module read port for Payroll's bank payment file. It returns the <b>plain</b> IBAN, which Employee otherwise only shows
/// masked, so its only caller is the permission-gated export (<c>payroll.bankfile.export</c>) - never a list or a card. Employees with
/// no primary account are simply absent from the result; the caller decides what a missing account means.
/// </summary>
public interface IEmployeePayoutAccountLookup
{
    Task<IReadOnlyList<EmployeePayoutAccount>> GetPrimaryPayoutAccountsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
