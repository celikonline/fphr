using Sense.Modules.Assets.Contracts.Events;
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
    DocumentRequirementMaterializationService materialization, IDocumentsUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<AssetAssigned>
{
    public async Task Handle(AssetAssigned integrationEvent, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await materialization.MaterializeAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, today, cancellationToken);
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
