using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Contracts;

/// <summary>
/// Cross-module write port other modules use to drop a file they generated themselves (e.g. Assets' zimmet
/// tutanağı PDF) straight into an employee's Documents folder, without depending on Documents' Domain or
/// Infrastructure or touching its DbContext directly (Modules_TalkOnlyThroughContracts). Mirrors
/// <see cref="IDocumentAttachmentLookup"/>'s read-only cross-module style, but for writes, and generalizes
/// Documents' own internal IVisaDocumentDeliveryService (visa template copies) so any module can deliver a file it
/// produced itself. The caller already has the finished file bytes - rendering/generation stays in the calling
/// module; this port only stores it and creates the catalog row.
/// </summary>
public interface ISystemDocumentDeliveryService
{
    /// <summary>
    /// Stores <paramref name="content"/> under the employee's folder and creates an Active Document row for it.
    /// <paramref name="sourceModule"/>/<paramref name="sourceRef"/> is the audit trail ("what produced this, and
    /// which record") - e.g. ("Assets", assetId.ToString()) for a zimmet tutanağı. Idempotent per
    /// (sourceModule, sourceRef): calling again for the same source replaces the previously delivered copy rather
    /// than creating a duplicate (e.g. a retried outbox event, or re-issuing a form after a correction).
    /// </summary>
    Task<Result<Guid>> DeliverGeneratedDocumentAsync(
        Guid tenantId,
        Guid employeeId,
        string title,
        string fileName,
        string contentType,
        byte[] content,
        string sourceModule,
        string sourceRef,
        CancellationToken cancellationToken = default);
}
