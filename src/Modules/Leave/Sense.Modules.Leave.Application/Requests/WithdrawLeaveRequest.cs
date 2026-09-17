using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

/// <summary>Requester withdraws a pending request (Workflow approval cancelled).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record WithdrawLeaveRequestCommand(Guid RequestId) : ICommand;

public sealed class WithdrawLeaveRequestHandler(ICurrentUser currentUser, ILeaveRequestRepository requests, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<WithdrawLeaveRequestCommand>
{
    public async Task<Result> Handle(WithdrawLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        if (currentUser.EmployeeId != request.EmployeeId)
        {
            return Error.Forbidden(LeaveRequestErrors.NotOwner);
        }

        var withdrawn = request.Withdraw(clock.GetUtcNow().UtcDateTime);
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        return request.ApprovalRequestId is { } approvalId ? await workflow.CancelAsync(approvalId, request.EmployeeId, cancellationToken) : Result.Success();
    }
}
