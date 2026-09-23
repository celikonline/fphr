using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure;

/// <summary>
/// <see cref="IDocumentRepository"/> + <see cref="IFileStorage"/> implementation of
/// <see cref="ISystemDocumentDeliveryService"/>: writes the caller's bytes to storage under a key namespaced by
/// (sourceModule, sourceRef) and adds a <see cref="Document.CreateSystemGeneratedCopy"/> row for it - same shape as
/// <c>VisaDocumentDeliveryService</c>, generalized to any module instead of only visa template copies. Added/deleted
/// documents are not saved here; the caller's own unit of work commits them (mirrors VisaDocumentDeliveryService's
/// remark - e.g. Documents' AssetAssignedDocumentRequirementHandler calls SaveChangesAsync once, covering both the
/// document requirement re-materialization and this delivery).
/// </summary>
public sealed class SystemDocumentDeliveryService(
    IDocumentRepository documents, IFileStorage fileStorage, ILogger<SystemDocumentDeliveryService> logger)
    : ISystemDocumentDeliveryService
{
    private const string BucketName = "documents";
    private const string DeliveredKeyPrefix = "system-deliveries";

    public async Task<Result<Guid>> DeliverGeneratedDocumentAsync(
        Guid tenantId,
        Guid employeeId,
        string title,
        string fileName,
        string contentType,
        byte[] content,
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken = default)
    {
        // Deterministic per (module, source, request): a retry after a partial failure overwrites the same storage
        // object instead of orphaning one, mirroring VisaDocumentDeliveryService's destinationKey comment. sourceRef
        // is a caller-controlled id (e.g. an AssetId), never free text, so it is safe as a key segment.
        var key = $"{DeliveredKeyPrefix}/{sourceModule}/{sourceRef}/{fileName}";

        try
        {
            await fileStorage.PutObjectAsync(tenantId, BucketName, key, contentType, content, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to store generated document for {SourceModule}/{SourceRef}", sourceModule, sourceRef);
            return Error.Failure(DocumentsDomainErrors.SystemDocumentDeliveryFailed);
        }

        var created = Document.CreateSystemGeneratedCopy(
            tenantId, employeeId, sourceModule, sourceRef, title, key, contentType, content.LongLength, BucketName);
        if (created.IsFailure)
        {
            return created.Error;
        }

        // Replace rather than duplicate: an at-least-once outbox retry (or a deliberate re-issue) must not pile up
        // copies of the same source's document.
        var existing = await documents.FindBySourceAsync(sourceModule, sourceRef, cancellationToken);
        existing?.Delete(Guid.Empty);

        documents.Add(created.Value);
        return created.Value.Id;
    }
}
