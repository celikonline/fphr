using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>
/// Workflow approved an "AssetAssignment" request (TK-018) -> the asset moves PendingAssignment -> Assigned and
/// AssetAssigned is enqueued (Documents reevaluates AssetHandoverForm from it - see AssignAssetCommand's doc comment).
/// </summary>
public sealed class AssetAssignmentApprovalCompletedHandler(IAssetRepository assets, IIntegrationEventOutbox outbox, IAssetsUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.AssetAssignment
            || await assets.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } asset)
        {
            return;
        }

        var issuedAt = asset.PendingIssuedAt ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (asset.CompleteAssignment(integrationEvent.ApprovalRequestId, issuedAt).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new AssetAssigned(asset.TenantId, asset.Id, asset.AssignedEmployeeId!.Value, asset.IssuedAt!.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected an "AssetAssignment" request -> the asset goes back to Unassigned; no event is published (nothing was ever assigned).</summary>
public sealed class AssetAssignmentApprovalRejectedHandler(IAssetRepository assets, IAssetsUnitOfWork unitOfWork)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.AssetAssignment
            || await assets.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } asset
            || asset.RejectAssignment(integrationEvent.ApprovalRequestId).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
