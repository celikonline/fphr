using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Payroll.Contracts;

/// <summary>One payslip of a paid period: which employee it belongs to.</summary>
public sealed record PaidPayslip(Guid PayslipId, Guid EmployeeId);

/// <summary>
/// Raised (Payroll outbox) when a period is marked paid - the moment 4857 md.37 requires the ücret pusulası to be handed
/// to each employee. Notifications e-mails every listed employee their payslip PDF. Amounts are not carried; consumers
/// render the document through <see cref="IPayslipDocumentProvider"/>.
/// </summary>
public sealed record PayrollPeriodPaid(Guid TenantId, Guid PeriodId, int Year, int Month, IReadOnlyList<PaidPayslip> Payslips)
    : IntegrationEvent(TenantId);

/// <summary>Renders one payslip's ücret pusulası as PDF (the same document the payslip download returns).</summary>
public interface IPayslipDocumentProvider
{
    /// <summary>The payslip PDF, or null when the payslip (or its period) does not exist for <paramref name="tenantId"/>.</summary>
    Task<ExportFileResult?> RenderPdfAsync(Guid tenantId, Guid payslipId, CancellationToken cancellationToken = default);
}
