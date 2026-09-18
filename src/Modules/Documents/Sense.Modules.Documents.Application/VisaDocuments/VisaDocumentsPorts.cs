using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>
/// Repository port for VisaDocumentRequest aggregate.
/// Abstracts persistence layer for visa requests.
/// </summary>
public interface IVisaDocumentRequestRepository
{
    /// <summary>Get a visa document request by ID.</summary>
    Task<VisaDocumentRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>Get requests for a specific employee, optionally filtered by status.</summary>
    Task<IEnumerable<VisaDocumentRequest>> GetByEmployeeIdAsync(
        Guid tenantId,
        Guid employeeId,
        VisaDocumentRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get requests pending approval for a specific approver.</summary>
    Task<IEnumerable<VisaDocumentRequest>> GetPendingApprovalAsync(
        Guid tenantId,
        Guid approverId,
        CancellationToken cancellationToken);

    /// <summary>Add a new visa document request to the repository.</summary>
    Task AddAsync(VisaDocumentRequest request, CancellationToken cancellationToken);

    /// <summary>Save all pending changes to the repository.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Service port for visa document delivery.
/// Handles copying template documents to employee folder on approval.
/// </summary>
public interface IDocumentDeliveryService
{
    /// <summary>
    /// Deliver template visa documents to employee folder.
    /// Called when visa request is approved.
    /// </summary>
    /// <returns>Number of documents delivered.</returns>
    Task<Result<int>> DeliverVisaDocumentsAsync(
        Guid tenantId,
        Guid employeeId,
        Guid visaRequestId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Port for tenant settings related to visa documents.
/// Manages approver configuration and template document IDs.
/// </summary>
public interface ITenantSettings
{
    /// <summary>
    /// Get the configured approver user ID for visa documents.
    /// Returns Guid.Empty if not configured.
    /// </summary>
    Task<Guid> GetVisaApproverAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Set the approver for visa documents.
    /// </summary>
    Task SetVisaApproverAsync(Guid tenantId, Guid approverId, CancellationToken cancellationToken);

    /// <summary>
    /// Get the list of template document IDs to deliver on visa request approval.
    /// </summary>
    Task<IEnumerable<Guid>> GetVisaTemplateDocumentsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Set the template documents for visa approval delivery.
    /// </summary>
    Task SetVisaTemplateDocumentsAsync(
        Guid tenantId,
        IEnumerable<Guid> templateDocumentIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Query port for visa documents.
/// Provides read-only access to visa request data for UI/reporting.
/// </summary>
public interface IVisaDocumentRequestQueries
{
    /// <summary>Get detail for a single visa request.</summary>
    Task<VisaDocumentRequestDto?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>Get requests for the current user.</summary>
    Task<IEnumerable<VisaDocumentRequestDto>> GetMyRequestsAsync(
        VisaDocumentRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get all requests for an employee (admin view).</summary>
    Task<IEnumerable<VisaDocumentRequestDto>> GetByEmployeeIdAsync(
        Guid employeeId,
        VisaDocumentRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get requests pending approval for the current user (approver view).</summary>
    Task<IEnumerable<VisaDocumentRequestDto>> GetPendingApprovalsAsync(
        CancellationToken cancellationToken);
}

/// <summary>DTO for visa document request (read model).</summary>
public sealed record VisaDocumentRequestDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    Guid? VisaDocumentProcessId,
    DateTime TravelStart,
    DateTime TravelEnd,
    string CountryCode,
    string VisaType,
    string Description,
    VisaDocumentRequestStatus Status,
    Guid? ApprovalRequestId,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    DateTime? CompletedAt);
