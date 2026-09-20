using FluentValidation;
using Sense.Modules.Documents.Contracts;
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


/// <summary>
/// Self-service leave request: validate, persist as Pending and start the Workflow "Leave" approval. With
/// <see cref="PreviousRequestId"/> it re-submits the caller's own rejected request (Source = Resubmission); the rejected record stays.
/// </summary>
[RequiresPermission("leave.my.requests")]
public sealed record SubmitLeaveRequestCommand(
    Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, Guid? PreviousRequestId = null,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null, Guid? SubstituteEmployeeId = null,
    IReadOnlyList<Guid>? DocumentIds = null)
    : ICommand<LeaveRequestDto>;

public sealed class SubmitLeaveRequestValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    public SubmitLeaveRequestValidator() => RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
}

public sealed class SubmitLeaveRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, LeaveRequestRules rules, ILeaveRequestRepository requests, ILeaveRequestAttachmentRepository attachments,
    IDocumentAttachmentLookup documents, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<SubmitLeaveRequestCommand, LeaveRequestDto>
{
    public async Task<Result<LeaveRequestDto>> Handle(SubmitLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        if (command.PreviousRequestId is { } previousId)
        {
            var previous = await requests.FindByIdAsync(previousId, cancellationToken);
            if (previous is null || previous.EmployeeId != employeeId || previous.Status != LeaveRequestStatus.Rejected)
            {
                return Error.Conflict(LeaveErrors.ResubmissionSourceInvalid);
            }
        }

        var valid = await rules.ValidateAsync(
            tenant.TenantId, employeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, null, cancellationToken,
            startTime: command.StartTime, endTime: command.EndTime, halfDayPeriod: command.HalfDayPeriod);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var substitute = await rules.ValidateSubstituteAsync(
            tenant.TenantId, employeeId, valid.Value.Type, command.SubstituteEmployeeId, command.StartDate, command.EndDate,
            command.StartTime, command.EndTime, command.HalfDayPeriod, cancellationToken);
        if (substitute.IsFailure)
        {
            return substitute.Error;
        }

        var documentIds = command.DocumentIds ?? [];
        var documentCheck = await rules.ValidateDocumentAsync(tenant.TenantId, valid.Value.Type, documentIds, cancellationToken);
        if (documentCheck.IsFailure)
        {
            return documentCheck.Error;
        }

        var submitted = LeaveRequest.Submit(
            tenant.TenantId, employeeId, valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days,
            command.Description, command.PreviousRequestId is null ? LeaveRequestSource.SelfService : LeaveRequestSource.Resubmission,
            clock.GetUtcNow().UtcDateTime, currentUser.IpAddress, null, command.PreviousRequestId, command.StartTime, command.EndTime, command.HalfDayPeriod,
            command.SubstituteEmployeeId);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var approval = await workflow.StartAsync(ApprovalRequestTypes.Leave, submitted.Value.Id, employeeId, null, currentUser.IpAddress, null, cancellationToken);
        if (approval.IsFailure)
        {
            return approval.Error;
        }

        submitted.Value.AttachApproval(approval.Value);
        requests.Add(submitted.Value);
        if (documentIds.Count > 0)
        {
            var attached = await LeaveRequestAttachmentLinker.LinkAsync(
                documents, attachments, tenant.TenantId, submitted.Value.Id, documentIds, currentUser.UserId, clock.GetUtcNow().UtcDateTime, cancellationToken);
            if (attached.IsFailure)
            {
                return attached.Error;
            }
        }

        return LeaveRequestMapper.ToDto(submitted.Value, valid.Value.Type.Name);
    }
}
