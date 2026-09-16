using Sense.Modules.Organization.Contracts;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Recruitment.Application.JobRequisitions;

/// <summary>Workflow approved a "Requisition" request → Approved (unlocks publishing any posting linked to it).</summary>
public sealed class RequisitionApprovalCompletedHandler(IJobRequisitionRepository requisitions, IRecruitmentUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Requisition
            || await requisitions.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } requisition
            || requisition.Approve(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected a "Requisition" request → Rejected.</summary>
public sealed class RequisitionApprovalRejectedHandler(IJobRequisitionRepository requisitions, IRecruitmentUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Requisition
            || await requisitions.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } requisition
            || requisition.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
