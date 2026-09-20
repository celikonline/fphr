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

/// <summary>
/// İşlemler → İzin Ekle: a manager/HR adds leave for an employee in scope (Source = OnBehalf). With <c>BypassApproval</c>
/// ("Onay sürecine dahil edilmeden onaylansın", needs leave.requests.bypass_approval) it is Approved at once and the Usage row
/// is posted; otherwise the Workflow "Leave" approval starts as for self-service.
/// </summary>
[RequiresPermission("leave.requests.create_on_behalf")]
public sealed record CreateLeaveRequestOnBehalfCommand(
    Guid EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, bool BypassApproval,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null, Guid? SubstituteEmployeeId = null)
    : ICommand<LeaveRequestDto>;

public sealed class CreateLeaveRequestOnBehalfValidator : AbstractValidator<CreateLeaveRequestOnBehalfCommand>
{
    public CreateLeaveRequestOnBehalfValidator() => RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
}

public sealed class CreateLeaveRequestOnBehalfHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, LeaveRequestRules rules, ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<CreateLeaveRequestOnBehalfCommand, LeaveRequestDto>
{
    public async Task<Result<LeaveRequestDto>> Handle(CreateLeaveRequestOnBehalfCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCreateOnBehalf.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        if (command.BypassApproval && !await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsBypassApproval.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveRequestErrors.BypassNotAllowed);
        }

        var valid = await rules.ValidateAsync(
            tenant.TenantId, command.EmployeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, null, cancellationToken,
            requireSelfService: false, startTime: command.StartTime, endTime: command.EndTime, halfDayPeriod: command.HalfDayPeriod);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var substitute = await rules.ValidateSubstituteAsync(
            tenant.TenantId, command.EmployeeId, valid.Value.Type, command.SubstituteEmployeeId, command.StartDate, command.EndDate,
            command.StartTime, command.EndTime, command.HalfDayPeriod, cancellationToken);
        if (substitute.IsFailure)
        {
            return substitute.Error;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var submitted = LeaveRequest.Submit(
            tenant.TenantId, command.EmployeeId, valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days,
            command.Description, LeaveRequestSource.OnBehalf, now, currentUser.IpAddress, null, null, command.StartTime, command.EndTime, command.HalfDayPeriod,
            command.SubstituteEmployeeId);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var request = submitted.Value;
        if (command.BypassApproval)
        {
            var approved = request.ApproveWithoutWorkflow(now);
            if (approved.IsFailure)
            {
                return approved.Error;
            }

            ledger.AddRange([LeaveLedgerEntry.Usage(request.TenantId, request.EmployeeId, request.LeaveTypeId, request.CalculatedDays, request.StartDate, LeaveLedgerSource.Request, request.Id)]);
        }
        else
        {
            var approval = await workflow.StartAsync(ApprovalRequestTypes.Leave, request.Id, command.EmployeeId, null, currentUser.IpAddress, null, cancellationToken);
            if (approval.IsFailure)
            {
                return approval.Error;
            }

            request.AttachApproval(approval.Value);
        }

        requests.Add(request);
        return LeaveRequestMapper.ToDto(request, valid.Value.Type.Name);
    }
}
