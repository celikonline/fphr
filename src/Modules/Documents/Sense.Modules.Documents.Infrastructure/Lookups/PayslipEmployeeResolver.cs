using Sense.Modules.Documents.Application.PayslipImports;
using Sense.Modules.Employee.Contracts;

namespace Sense.Modules.Documents.Infrastructure.Lookups;

/// <summary>
/// Resolves a payslip file's registration number through <see cref="IEmployeeLookup"/>. The lookup only sees active
/// (non-terminated) employees and returns null for both "no such sicil" and "sicil belongs to a terminated employee",
/// so <see cref="PayslipEmployeeMatchKind.Terminated"/> is not produced until Employee.Contracts exposes a
/// status-aware sicil lookup; such files are reported as unknown registration for now.
/// </summary>
public sealed class PayslipEmployeeResolver(IEmployeeLookup employees) : IPayslipEmployeeResolver
{
    public async Task<PayslipEmployeeMatch> ResolveAsync(Guid tenantId, string registrationNumber, CancellationToken cancellationToken = default)
    {
        var id = await employees.FindActiveBySicilNumarasiAsync(tenantId, registrationNumber, cancellationToken);
        return id is { } employeeId
            ? new PayslipEmployeeMatch(PayslipEmployeeMatchKind.Active, employeeId)
            : PayslipEmployeeMatch.Unknown;
    }
}
