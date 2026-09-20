using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Application.VisaDocuments;

/// <summary>
/// Workflow approved a "VisaDocument" request: delivers the configured template documents into the employee's folder
/// and completes the request. Handlers run in Sense.Worker (OutboxProcessor publishes ApprovalCompleted there), so this
/// is a plain integration event handler - it never goes through the permission-checked command pipeline.
/// Delivery happens before any state change: if it fails the request is untouched and the exception makes the outbox
/// retry the message with backoff (a half-approved row must never be flushed by a later save in the same scope).
/// </summary>
public sealed class VisaApprovalCompletedHandler(
    IVisaDocumentRequestRepository requests,
    IVisaDocumentDeliveryService delivery,
    IDocumentsUnitOfWork unitOfWork,
    ILogger<VisaApprovalCompletedHandler> logger) : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != SubmitVisaDocumentRequestHandler.RequestType
            || await requests.GetByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Status != VisaDocumentRequestStatus.AwaitingApproval)
        {
            // Not ours, unknown subject, or a redelivered event for a request that already left AwaitingApproval.
            return;
        }

        var delivered = await delivery.DeliverVisaDocumentsAsync(request.TenantId, request.EmployeeId, request.Id, cancellationToken);
        if (delivered.IsFailure)
        {
            logger.LogError("Visa document delivery failed for request {RequestId}: {ErrorCode}", request.Id, delivered.Error.Code);
            throw new InvalidOperationException($"Visa document delivery failed for request {request.Id}: {delivered.Error.Code}");
        }

        request.Approve();
        request.Complete();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Visa document request {RequestId} completed ({Count} documents delivered)", request.Id, delivered.Value);
    }
}

/// <summary>Workflow rejected a "VisaDocument" request: marks it Rejected. ApprovalRejected carries no reason, so the
/// request's RejectionReason stays empty and the web shows a generic "rejected by the approver" message.</summary>
public sealed class VisaApprovalRejectedHandler(IVisaDocumentRequestRepository requests, IDocumentsUnitOfWork unitOfWork)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != SubmitVisaDocumentRequestHandler.RequestType
            || await requests.GetByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Status != VisaDocumentRequestStatus.AwaitingApproval)
        {
            return;
        }

        request.Reject(null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
