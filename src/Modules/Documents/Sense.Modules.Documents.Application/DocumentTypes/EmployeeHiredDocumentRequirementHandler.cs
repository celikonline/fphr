using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>
/// EmployeeHired → materializes the employee's Onboarding + matching-Conditional document requirements (catalog
/// seeded lazily on first use). Idempotent: EmployeeDocumentRequirement.FindAsync (inside
/// DocumentRequirementMaterializationService) skips any type that already has a row, so an outbox redelivery is
/// harmless. Runs outside the MediatR command pipeline (integration event handlers are dispatched directly by
/// InProcessEventBus/OutboxProcessor - see UnitOfWorkBehaviour, which only wraps ICommand/ICommand&lt;T&gt;
/// requests), so this handler must save explicitly, mirroring Leave's EmployeeHiredHandler.
/// </summary>
public sealed class EmployeeHiredDocumentRequirementHandler(
    DocumentRequirementMaterializationService materialization,
    IDocumentsUnitOfWork unitOfWork) : IIntegrationEventHandler<EmployeeHired>
{
    public async Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        await materialization.MaterializeAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, integrationEvent.HireDate, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
