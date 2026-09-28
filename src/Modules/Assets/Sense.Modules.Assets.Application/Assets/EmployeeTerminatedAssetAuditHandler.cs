using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>
/// EmployeeTerminated (Employee module) - for each of the employee's still-open assets (ListByEmployeeAsync only
/// ever returns currently-Assigned ones, see its remark), writes an AssetUnreturnedAtTermination event to this
/// module's own outbox: an explicit, timestamped audit trail entry ("zimmet iade edilmeden çıkış") without blocking
/// the termination itself (Employee.Terminate() is untouched - faz4 offboarding↔zimmet task, read-side only).
/// Runs outside the MediatR command pipeline (integration event handlers are dispatched directly by
/// InProcessEventBus/OutboxProcessor), so this handler must save explicitly, mirroring Documents'
/// EmployeeHiredDocumentRequirementHandler.
/// </summary>
public sealed class EmployeeTerminatedAssetAuditHandler(
    IAssetRepository assets,
    IIntegrationEventOutbox outbox,
    IAssetsUnitOfWork unitOfWork) : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        var open = await assets.ListByEmployeeAsync(integrationEvent.EmployeeId, cancellationToken);
        foreach (var asset in open)
        {
            // Idempotent: a redelivered/duplicately registered handler must not write the audit event twice.
            if (await assets.HasUnreturnedTerminationAuditAsync(asset.TenantId, asset.Id, integrationEvent.EmployeeId, cancellationToken))
            {
                continue;
            }

            outbox.Enqueue(new AssetUnreturnedAtTermination(asset.TenantId, asset.Id, integrationEvent.EmployeeId, asset.Name));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
