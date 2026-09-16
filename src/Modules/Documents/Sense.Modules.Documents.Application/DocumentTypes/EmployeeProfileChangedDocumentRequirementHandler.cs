using Sense.Modules.Employee.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>
/// EmployeeProfileChanged (Employee module) -> re-runs the same materialization logic
/// <see cref="ReevaluateEmployeeDocumentRequirementsCommand"/> uses, for the affected employee, so a Conditional
/// requirement (AgeUnder18/HasDisability today) appears once its condition starts matching, and a stale Missing one
/// gets auto-waived once it stops (see <see cref="DocumentRequirementMaterializationService.AutoWaivedReason"/>).
/// Mirrors <see cref="AssetAssignedDocumentRequirementHandler"/>: no real hire date is available here, so "today" is
/// used as the grace-period basis for any newly-resolved requirement.
///
/// Known gap (faz4-durum-takibi.md, cross-module - not this module's file to fix): today's
/// <see cref="EmployeeProfileSections"/> values (Bank/CustomFields/SelfService/Union) don't cover the core HR "update
/// employee" path that actually edits BirthDate/DisabilityDegree - the fields the eligibility signals derive from -
/// so that path raises no integration event yet and this handler won't fire for it. Wiring the consumer now is still
/// correct and forward-looking (MaterializeAsync is idempotent, and SelfService edits already go through it); it
/// starts covering the BirthDate/DisabilityDegree case automatically once Employee's core update handler (Faz 2's
/// file) raises EmployeeProfileChanged for that path too.
/// </summary>
public sealed class EmployeeProfileChangedDocumentRequirementHandler(
    DocumentRequirementMaterializationService materialization, IDocumentsUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<EmployeeProfileChanged>
{
    public async Task Handle(EmployeeProfileChanged integrationEvent, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await materialization.MaterializeAsync(integrationEvent.TenantId, integrationEvent.EmployeeId, today, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
