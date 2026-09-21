using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// Implements <see cref="IEmployeePayoutAccountLookup"/>: each employee's primary bank account with the IBAN revealed (decrypted).
/// Read-only; the reveal happens here so the ciphertext never leaves the Employee module, and only the permission-gated payroll
/// bank-file export calls it.
/// </summary>
public sealed class EmployeePayoutAccountLookup(EmployeeDbContext context, IIbanProtector protector) : IEmployeePayoutAccountLookup
{
    public async Task<IReadOnlyList<EmployeePayoutAccount>> GetPrimaryPayoutAccountsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return [];
        }

        var accounts = await context.Set<BankAccount>().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.IsPrimary && employeeIds.Contains(a.EmployeeId))
            .Select(a => new { a.EmployeeId, a.ProtectedIban, a.BankName })
            .ToListAsync(cancellationToken);

        return accounts.Select(a => new EmployeePayoutAccount(a.EmployeeId, protector.Reveal(a.ProtectedIban), a.BankName)).ToList();
    }
}
