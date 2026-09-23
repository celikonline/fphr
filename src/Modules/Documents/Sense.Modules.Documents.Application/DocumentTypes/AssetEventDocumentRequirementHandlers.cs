using Microsoft.Extensions.Logging;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>
/// AssetAssigned/AssetReturned (Assets module, roadmap #65) -> re-runs the same materialization logic
/// ReevaluateEmployeeDocumentRequirementsCommand uses, for the affected employee, so the AssetHandoverForm
/// conditional requirement (Condition=HasOpenAssets) appears/disappears automatically as assets get assigned/
/// returned. No real hire date is available here either, so "today" is used as the grace-period basis, mirroring
/// ReevaluateEmployeeDocumentRequirementsHandler's documented simplification. Runs outside the MediatR command
/// pipeline (integration event handlers are dispatched directly by InProcessEventBus/OutboxProcessor - see
/// UnitOfWorkBehaviour, which only wraps ICommand/ICommand&lt;T&gt; requests), so both handlers must save explicitly,
/// mirroring EmployeeHiredDocumentRequirementHandler.
/// </summary>
public sealed class AssetAssignedDocumentRequirementHandler(
    DocumentRequirementMaterializationService materialization,
    IAssetHandoverFormLookup handoverForms,
    ISystemDocumentDeliveryService delivery,
    IDocumentsUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AssetAssignedDocumentRequirementHandler> logger)
    : IIntegrationEventHandler<AssetAssigned>
{
    private const string SourceModule = "Assets";

    public async Task Handle(AssetAssigned integrationEvent, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await materialization.MaterializeAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, today, cancellationToken);

        // Auto-deliver the "AssetHandoverForm" requirement's own file (#120): render the same PDF
        // GET /assets/{id}/handover-form serves and drop it straight into the employee's Documents folder. HR's
        // manual download-sign-upload path still works, it just isn't the only way anymore. Null means the asset no
        // longer resolves as assigned (e.g. a stale/redelivered event after a later Return) - nothing to deliver.
        var form = await handoverForms.RenderAsync(integrationEvent.TenantId, integrationEvent.AssetId, cancellationToken);
        if (form is not null)
        {
            var delivered = await delivery.DeliverGeneratedDocumentAsync(
                integrationEvent.TenantId,
                integrationEvent.EmployeeId,
                title: form.FileName,
                fileName: form.FileName,
                contentType: form.ContentType,
                content: form.Content,
                sourceModule: SourceModule,
                sourceRef: integrationEvent.AssetId.ToString(),
                cancellationToken);
            if (delivered.IsFailure)
            {
                // Mirrors VisaApprovalCompletedHandler: fail loudly so the outbox retries with backoff, rather than
                // silently leaving the requirement materialized but its document missing.
                logger.LogError(
                    "Zimmet tutanağı delivery failed for asset {AssetId}: {ErrorCode}", integrationEvent.AssetId, delivered.Error.Code);
                throw new InvalidOperationException(
                    $"Zimmet tutanağı delivery failed for asset {integrationEvent.AssetId}: {delivered.Error.Code}");
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AssetReturnedDocumentRequirementHandler(
    DocumentRequirementMaterializationService materialization, IDocumentsUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<AssetReturned>
{
    public async Task Handle(AssetReturned integrationEvent, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await materialization.MaterializeAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, today, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
