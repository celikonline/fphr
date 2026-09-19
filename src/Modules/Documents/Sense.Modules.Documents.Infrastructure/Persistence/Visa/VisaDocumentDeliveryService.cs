using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure.Persistence.Visa;

/// <summary>
/// <see cref="IDocumentRepository"/> + <see cref="IFileStorage"/> implementation of
/// <see cref="IVisaDocumentDeliveryService"/>: for every template document id configured on
/// <see cref="VisaSettings"/>, server-side copies its storage object into a per-request key and adds a new
/// <see cref="Document"/> row tagged with the employee and the visa request as its source
/// (<see cref="Document.CreateDeliveredCopy"/>) - the audit trail the handoff doc asks for. Reads through
/// <see cref="IDocumentRepository"/> (one call per configured template - lists are small, a handful at most) rather
/// than the DbContext directly, so this stays unit-testable with the same NSubstitute-mocked-port pattern as every
/// other handler in this module. Added documents are not saved here; the caller's own unit of work commits them
/// alongside the VisaDocumentRequest state change.
/// </summary>
public sealed class VisaDocumentDeliveryService(
    IDocumentRepository documents,
    IVisaSettingsProvider visaSettings,
    IFileStorage fileStorage,
    ILogger<VisaDocumentDeliveryService> logger) : IVisaDocumentDeliveryService
{
    /// <summary>Storage key prefix for delivered visa document copies, namespaced by request so two requests never
    /// collide even if they deliver the same template.</summary>
    private const string DeliveredKeyPrefix = "visa-deliveries";

    public async Task<Result<int>> DeliverVisaDocumentsAsync(
        Guid tenantId,
        Guid employeeId,
        Guid visaRequestId,
        CancellationToken cancellationToken)
    {
        var templateIds = await visaSettings.GetTemplateDocumentIdsAsync(tenantId, cancellationToken);
        if (templateIds.Count == 0)
        {
            return Result.Success(0);
        }

        var delivered = 0;
        foreach (var templateId in templateIds)
        {
            var template = await ResolveActiveTemplateAsync(templateId, visaRequestId, cancellationToken);
            if (template is null)
            {
                continue;
            }

            var destinationKey = $"{DeliveredKeyPrefix}/{visaRequestId:N}/{Guid.CreateVersion7():N}-{template.Title}";

            try
            {
                await fileStorage.CopyObjectAsync(
                    tenantId, template.BucketName, template.FileKey, template.BucketName, destinationKey, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(
                    ex, "Failed to copy visa template document {DocumentId} for request {RequestId}", template.Id, visaRequestId);
                return Error.Failure(VisaDomainErrors.VisaDocumentDeliveryFailed);
            }

            var copy = Document.CreateDeliveredCopy(
                tenantId, employeeId, visaRequestId, template.Title, destinationKey, template.ContentType, template.Size, template.BucketName);

            if (copy.IsFailure)
            {
                // Only fails on data that would also have rejected the original template (blank title/key/employee);
                // that means the template itself is malformed, not a transient failure - skip it and keep going
                // rather than blocking delivery of the other, valid templates.
                logger.LogError(
                    "Failed to create delivered copy of document {DocumentId} for request {RequestId}: {ErrorCode}",
                    template.Id, visaRequestId, copy.Error.Code);
                continue;
            }

            documents.Add(copy.Value);
            delivered++;
        }

        return Result.Success(delivered);
    }

    private async Task<Document?> ResolveActiveTemplateAsync(Guid templateId, Guid visaRequestId, CancellationToken cancellationToken)
    {
        var result = await documents.GetByIdAsync(templateId, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Visa request {RequestId}: configured template document {DocumentId} no longer resolves to a document",
                visaRequestId, templateId);
            return null;
        }

        if (result.Value.Status != DocumentStatus.Active)
        {
            logger.LogWarning(
                "Visa request {RequestId}: configured template document {DocumentId} is not Active (status {Status})",
                visaRequestId, templateId, result.Value.Status);
            return null;
        }

        return result.Value;
    }
}
