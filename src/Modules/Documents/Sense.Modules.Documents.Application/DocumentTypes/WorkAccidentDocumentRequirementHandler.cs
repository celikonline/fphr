using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.OccupationalSafety.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Documents.Application.DocumentTypes;

/// <summary>
/// WorkAccidentRecorded (OccupationalSafety) -> the injured employee's "İş kazası tutanağı" requirement, due on the SGK
/// notification deadline. Only when the tenant's WorkAccidentReport type is active; an existing requirement for the type
/// (one row per employee + type) is left as is, so a second accident does not reopen an already uploaded report -
/// a known limit of the one-row-per-type model. Saves explicitly (integration handlers run outside the command pipeline).
/// </summary>
public sealed class WorkAccidentDocumentRequirementHandler(
    DocumentTypeProvisioningService provisioning,
    IDocumentTypeDefinitionRepository types,
    IEmployeeDocumentRequirementRepository requirements,
    IDocumentsUnitOfWork unitOfWork) : IIntegrationEventHandler<WorkAccidentRecorded>
{
    public const string WorkAccidentReportCode = "WorkAccidentReport";

    public async Task Handle(WorkAccidentRecorded integrationEvent, CancellationToken cancellationToken)
    {
        await provisioning.EnsureSeededAsync(integrationEvent.TenantId, cancellationToken);
        var type = (await types.ListAsync(stage: null, isActive: true, cancellationToken))
            .FirstOrDefault(t => t.Code == WorkAccidentReportCode);
        if (type is null
            || await requirements.FindAsync(integrationEvent.EmployeeId, type.Id, cancellationToken) is not null)
        {
            return;
        }

        var materialized = EmployeeDocumentRequirement.Materialize(
            integrationEvent.TenantId, integrationEvent.EmployeeId, type.Id, integrationEvent.SgkNotificationDeadline);
        if (materialized.IsFailure)
        {
            return;
        }

        requirements.Add(materialized.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
