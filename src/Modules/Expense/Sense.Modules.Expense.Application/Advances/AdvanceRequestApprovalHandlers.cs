using Sense.Modules.Expense.Application.ExpenseClaims;
using Sense.Modules.Expense.Contracts.Events;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Expense.Application.Advances;

/// <summary>Workflow approved an "Advance" request -> Approved.</summary>
public sealed class AdvanceApprovalCompletedHandler(IAdvanceRequestRepository requests, IIntegrationEventOutbox outbox, IExpenseUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ExpenseRequestTypes.Advance
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } advance
            || advance.Approve(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new AdvanceRequestApproved(advance.TenantId, advance.Id, advance.EmployeeId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected an "Advance" request -> Rejected.</summary>
public sealed class AdvanceApprovalRejectedHandler(IAdvanceRequestRepository requests, IIntegrationEventOutbox outbox, IExpenseUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ExpenseRequestTypes.Advance
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } advance
            || advance.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new AdvanceRequestRejected(advance.TenantId, advance.Id, advance.EmployeeId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
