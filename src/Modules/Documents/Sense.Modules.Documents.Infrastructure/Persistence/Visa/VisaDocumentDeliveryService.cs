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

        // Rows are only added once every copy succeeded: a failure part-way must not leave some of them tracked
        // (a later SaveChanges in the same scope would persist them and a retry would then duplicate them).
        var copies = new List<Document>(templateIds.Count);
        foreach (var templateId in templateIds)
        {
            var template = await ResolveActiveTemplateAsync(templateId, visaRequestId, cancellationToken);
            if (template is null)
            {
                continue;
            }

            // Deterministic per (request, template): a retry after a partial failure overwrites instead of orphaning objects, and the
            // key never embeds user-supplied text (only a sanitized extension), so the storage adapter cannot reject it.
            var destinationKey = $"{DeliveredKeyPrefix}/{visaRequestId:N}/{template.Id:N}{SafeExtension(template.Title)}";

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

            copies.Add(copy.Value);
        }

        foreach (var copy in copies)
        {
            documents.Add(copy);
        }

        return Result.Success(copies.Count);
    }

    private const int MaxExtensionLength = 10;

    /// <summary>File extension of the (user-supplied) title, restricted to letters/digits so it is always a valid storage key
    /// suffix; empty when there is none. The document row keeps the original title.</summary>
    public static string SafeExtension(string title)
    {
        var extension = Path.GetExtension(title);
        if (extension.Length is 0 or > MaxExtensionLength + 1 || !extension.Skip(1).All(char.IsAsciiLetterOrDigit))
        {
            return string.Empty;
        }

        return extension.ToLowerInvariant();
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

        // A template is a plain tenant document. System-delivered copies (someone's personal folder) and anything tied to
        // another employee must never be re-delivered even if an id slipped into the settings.
        if (result.Value.EmployeeId is not null || result.Value.VisaDocumentRequestId is not null)
        {
            logger.LogWarning(
                "Visa request {RequestId}: configured template document {DocumentId} belongs to an employee folder and is skipped",
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
