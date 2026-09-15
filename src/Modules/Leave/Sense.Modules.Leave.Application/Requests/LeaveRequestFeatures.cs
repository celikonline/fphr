using FluentValidation;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

public static class LeaveRequestErrors
{
    public const string TypeNotAvailable = "leave.errors.type_not_available";
    public const string Overlap = "leave.errors.overlap";
    public const string AbovePerRequestLimit = "leave.errors.above_per_request_limit";
    public const string BelowMinDays = "leave.errors.below_min_days";
    public const string AboveMaxDays = "leave.errors.above_max_days";
    public const string InsufficientBalance = "leave.errors.insufficient_balance";
    public const string DescriptionRequired = "leave.errors.description_required";
    public const string NotOwner = "leave.request.not_owner";
}

public sealed record LeaveRequestDto(
    Guid Id, Guid EmployeeId, Guid LeaveTypeId, LeaveTypeCode LeaveTypeCode, DateOnly StartDate, DateOnly EndDate, decimal CalculatedDays,
    string? Description, LeaveRequestStatus Status, LeaveRequestSource Source, Guid? ApprovalRequestId, int RevisionNo, DateTime SubmittedAt, DateTime? DecidedAt);

internal static class LeaveRequestMapper
{
    public static LeaveRequestDto ToDto(LeaveRequest r) =>
        new(r.Id, r.EmployeeId, r.LeaveTypeId, r.LeaveTypeCodeSnapshot, r.StartDate, r.EndDate, r.CalculatedDays, r.Description, r.Status, r.Source,
            r.ApprovalRequestId, r.RevisionNo, r.SubmittedAt, r.DecidedAt);
}

/// <summary>Validation shared by submit and edit (11.3.5 "Talep doğrulama" steps 2, 4, 6, 7, 9, 11, 12).</summary>
public sealed class LeaveRequestRules(
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger,
    ILeaveDayCalculator dayCalculator,
    LeaveBalanceService balances)
{
    public async Task<Result<(LeaveType Type, decimal Days)>> ValidateAsync(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, string? description, Guid? editedRequestId, CancellationToken ct)
    {
        if (end < start)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        var profile = await profiles.FindByEmployeeIdAsync(employeeId, ct);
        if (profile is null)
        {
            return Error.NotFound(LeaveErrors.ProfileNotFound);
        }

        var set = await ruleSets.GetByIdAsync(profile.RuleSetId, ct);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var type = set.Value.Types.FirstOrDefault(t => t.Id == leaveTypeId);
        if (type is null || type.IsDisabled || !type.AllowSelfServiceRequest)
        {
            return Error.Validation(LeaveRequestErrors.TypeNotAvailable);
        }

        if (type.RequiresDescription && string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation(LeaveRequestErrors.DescriptionRequired);
        }

        var days = (await dayCalculator.CalculateAsync(tenantId, employeeId, start, end, type.CountsNonWorkingDays, ct)).Days;
        if (days <= 0m)
        {
            return Error.Validation(LeaveDomainErrors.RequestZeroDays);
        }

        var overlapping = await requests.ListActiveOverlappingAsync(employeeId, start, end, ct);
        if (overlapping.Any(r => r.Id != editedRequestId))
        {
            return Error.Conflict(LeaveRequestErrors.Overlap);
        }

        if (type.LimitType == LeaveLimitType.PerRequest && days > type.PerRequestMaxDays)
        {
            return Error.Validation(LeaveRequestErrors.AbovePerRequestLimit, ("max", type.PerRequestMaxDays));
        }

        if (type.MinConsecutiveDays is { } min && days < min)
        {
            return Error.Validation(LeaveRequestErrors.BelowMinDays, ("min", min));
        }

        if (type.MaxConsecutiveDays is { } max && days > max)
        {
            return Error.Validation(LeaveRequestErrors.AboveMaxDays, ("max", max));
        }

        if (type.HasBalance && type.NegativeBalance != NegativeBalanceMode.Unlimited)
        {
            var entries = await ledger.ListAsync(employeeId, type.Id, ct);
            var projected = LeaveBalanceEngine.Project(type, profile.LeaveAccrualStartDate, balances.Today, entries);
            var pending = await requests.SumPendingDaysAsync(employeeId, type.Id, ct);
            if (editedRequestId is { } editedId && await requests.FindByIdAsync(editedId, ct) is { Status: LeaveRequestStatus.Pending } edited && edited.LeaveTypeId == type.Id)
            {
                pending -= edited.CalculatedDays;
            }

            var floor = type.NegativeBalance == NegativeBalanceMode.Limited ? -(type.NegativeBalanceLimitDays ?? 0m) : 0m;
            if (projected.AvailableBalance - pending - days < floor)
            {
                return Error.Validation(LeaveRequestErrors.InsufficientBalance, ("available", projected.AvailableBalance - pending));
            }
        }

        return (type, days);
    }
}

/// <summary>Self-service leave request: validate, persist as Pending and start the Workflow "Leave" approval.</summary>
[RequiresPermission("leave.my.requests")]
public sealed record SubmitLeaveRequestCommand(Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description) : ICommand<Guid>;

public sealed class SubmitLeaveRequestValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    public SubmitLeaveRequestValidator() => RuleFor(x => x.Description).MaximumLength(LeaveLimits.DescriptionMaxLength);
}

public sealed class SubmitLeaveRequestHandler(
    ITenantContext tenant, ICurrentUser currentUser, LeaveRequestRules rules, ILeaveRequestRepository requests, IWorkflowService workflow, TimeProvider clock)
    : ICommandHandler<SubmitLeaveRequestCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var valid = await rules.ValidateAsync(tenant.TenantId, employeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, null, cancellationToken);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var submitted = LeaveRequest.Submit(
            tenant.TenantId, employeeId, valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days,
            command.Description, LeaveRequestSource.SelfService, clock.GetUtcNow().UtcDateTime, currentUser.IpAddress, null);
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
        return submitted.Value.Id;
    }
}

/// <summary>UpdatePendingLeaveRequest: requester edits a pending request; the old approval is cancelled and a new one started (RevisionNo++).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record UpdatePendingLeaveRequestCommand(Guid RequestId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description) : ICommand;

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

        var valid = await rules.ValidateAsync(tenant.TenantId, request.EmployeeId, command.LeaveTypeId, command.StartDate, command.EndDate, command.Description, request.Id, cancellationToken);
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

        var updated = request.UpdatePending(valid.Value.Type.Id, valid.Value.Type.Code, command.StartDate, command.EndDate, valid.Value.Days, command.Description);
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

[RequiresPermission("leave.my.requests")]
public sealed record ListMyLeaveRequestsQuery : IQuery<IReadOnlyList<LeaveRequestDto>>;

public sealed class ListMyLeaveRequestsHandler(ICurrentUser currentUser, ILeaveRequestRepository requests) : IQueryHandler<ListMyLeaveRequestsQuery, IReadOnlyList<LeaveRequestDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(ListMyLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        IReadOnlyList<LeaveRequestDto> list = (await requests.ListByEmployeeAsync(employeeId, cancellationToken)).Select(LeaveRequestMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

[RequiresPermission("leave.requests.read")]
public sealed record ListEmployeeLeaveRequestsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<LeaveRequestDto>>;

public sealed class ListEmployeeLeaveRequestsHandler(ILeaveRequestRepository requests, IDataScopeService dataScope)
    : IQueryHandler<ListEmployeeLeaveRequestsQuery, IReadOnlyList<LeaveRequestDto>>
{
    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(ListEmployeeLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        IReadOnlyList<LeaveRequestDto> list = (await requests.ListByEmployeeAsync(query.EmployeeId, cancellationToken)).Select(LeaveRequestMapper.ToDto).ToList();
        return Result.Success(list);
    }
}

/// <summary>Workflow approved a "Leave" request → Approved + Usage ledger row dated at the leave start date.</summary>
public sealed class LeaveApprovalCompletedHandler(ILeaveRequestRepository requests, ILeaveLedgerRepository ledger, ILeaveUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Leave
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Approve(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        ledger.AddRange([LeaveLedgerEntry.Usage(request.TenantId, request.EmployeeId, request.LeaveTypeId, request.CalculatedDays, request.StartDate, LeaveLedgerSource.Request, request.Id)]);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected a "Leave" request → Rejected (its reservation disappears).</summary>
public sealed class LeaveApprovalRejectedHandler(ILeaveRequestRepository requests, ILeaveUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.Leave
            || await requests.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } request
            || request.Reject(integrationEvent.ApprovalRequestId, clock.GetUtcNow().UtcDateTime).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
