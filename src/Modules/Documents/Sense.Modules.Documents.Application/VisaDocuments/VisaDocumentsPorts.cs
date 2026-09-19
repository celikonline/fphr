using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>Persistence port for the <see cref="VisaDocumentRequest"/> aggregate.</summary>
public interface IVisaDocumentRequestRepository
{
    Task<VisaDocumentRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VisaDocumentRequest>> GetByEmployeeIdAsync(
        Guid tenantId,
        Guid employeeId,
        VisaDocumentRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(VisaDocumentRequest request, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Persistence port for the <see cref="VisaDocumentProcess"/> aggregate.</summary>
public interface IVisaDocumentProcessRepository
{
    Task<VisaDocumentProcess?> GetByIdAsync(Guid processId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VisaDocumentProcess>> GetActiveAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(VisaDocumentProcess process, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Persistence port for the <see cref="VisaSettings"/> aggregate (one row per tenant). Mirrors
/// Attendance's <c>IShiftSettingsRepository</c>: <see cref="FindAsync"/> is tenant-scoped implicitly (via the
/// DbContext's global filter), no explicit tenantId parameter needed.</summary>
public interface IVisaSettingsRepository
{
    void Add(VisaSettings settings);

    Task<VisaSettings?> FindAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the tenant's visa-specific settings (approver and the template documents copied
/// into the employee folder once a request is approved).
/// </summary>
public interface IVisaSettingsProvider
{
    /// <summary>Configured approver, as an <c>EmployeeId</c> (Workflow steps address employees, not users).
    /// Null or <see cref="Guid.Empty"/> means "not configured".</summary>
    Task<Guid?> GetApproverEmployeeIdAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Template documents delivered to the employee on approval. Empty is valid (nothing to deliver).</summary>
    Task<IReadOnlyList<Guid>> GetTemplateDocumentIdsAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Copies the configured visa template documents into an employee's folder on approval.</summary>
public interface IVisaDocumentDeliveryService
{
    /// <returns>Number of documents delivered.</returns>
    Task<Result<int>> DeliverVisaDocumentsAsync(
        Guid tenantId,
        Guid employeeId,
        Guid visaRequestId,
        CancellationToken cancellationToken);
}

/// <summary>Read model for a visa document request.</summary>
public sealed record VisaDocumentRequestDto(
    Guid Id,
    Guid EmployeeId,
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
