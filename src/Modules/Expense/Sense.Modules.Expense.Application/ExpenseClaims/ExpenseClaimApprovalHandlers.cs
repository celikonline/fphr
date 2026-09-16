using Sense.Modules.Expense.Contracts.Events;
using Sense.Modules.Expense.Domain;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Expense.Application.ExpenseClaims;

/// <summary>Workflow approved an "ExpenseClaim" request -> Approved; a domain ExpenseClaimApproved event lands on this module's own outbox.</summary>
public sealed class ExpenseClaimApprovalCompletedHandler(IExpenseClaimRepository claims, IIntegrationEventOutbox outbox, IExpenseUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ExpenseRequestTypes.Claim
            || await claims.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } claim
            || claim.Approve(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new ExpenseClaimApproved(claim.TenantId, claim.Id, claim.EmployeeId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected an "ExpenseClaim" request -> Rejected.</summary>
public sealed class ExpenseClaimApprovalRejectedHandler(IExpenseClaimRepository claims, IIntegrationEventOutbox outbox, IExpenseUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ExpenseRequestTypes.Claim
            || await claims.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } claim
            || claim.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        outbox.Enqueue(new ExpenseClaimRejected(claim.TenantId, claim.Id, claim.EmployeeId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
