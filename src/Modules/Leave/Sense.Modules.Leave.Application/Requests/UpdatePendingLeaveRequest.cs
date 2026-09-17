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

/// <summary>UpdatePendingLeaveRequest: requester edits a pending request; the old approval is cancelled and a new one started (RevisionNo++).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record UpdatePendingLeaveRequestCommand(
    Guid RequestId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null) : ICommand;

public sealed class UpdatePendingLeaveRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, LeaveRequestRules rules, ILeaveRequestRepository requests, IWorkflowService workflow)
    : ICommandHandler<UpdatePendingLeaveRequestCommand>
{
    public async Task<Result> Handle(UpdatePendingLeaveRequestCommand command, CancellationToken cancellationToken)
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

        if (request.Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        var valid = await rules.ValidateAsync(
            tenant.TenantId, request.EmployeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, request.Id, cancellationToken,
            startTime: command.StartTime, endTime: command.EndTime, halfDayPeriod: command.HalfDayPeriod);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        if (request.ApprovalRequestId is { } previousApproval)
        {
            var cancelled = await workflow.CancelAsync(previousApproval, request.EmployeeId, cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }
        }

        var updated = request.UpdatePending(
            valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days, command.Description,
            command.StartTime, command.EndTime, command.HalfDayPeriod);
        if (updated.IsFailure)
        {
            return updated;
        }

        var approval = await workflow.StartAsync(ApprovalRequestTypes.Leave, request.Id, request.EmployeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        request.AttachApproval(approval.Value);
        return Result.Success();
    }
}
