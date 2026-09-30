using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Contracts.Events;

/// <summary>
/// Raised (Documents outbox) when an externally produced payslip PDF was matched to an employee by registration number
/// and delivered into their document folder (E3). Notifications maps it to <c>payroll.payslip_document_uploaded</c>.
/// </summary>
public sealed record PayslipDocumentUploaded(Guid TenantId, Guid EmployeeId, Guid DocumentId, int Year, int Month)
    : IntegrationEvent(TenantId);
