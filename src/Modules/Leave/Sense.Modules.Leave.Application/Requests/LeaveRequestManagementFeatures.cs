using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

/// <summary>
/// Decision #16: an authorized user cancels a pending (its Workflow approval is cancelled) or approved leave request (any dates).
/// Posted usage is restored with a Reversal ledger row, balances are regenerated and Leave.LeaveCancelled is published.
/// </summary>
[RequiresPermission("leave.requests.cancel_approved")]
public sealed record CancelLeaveRequestCommand(Guid EmployeeId, Guid RequestId, string Reason) : ICommand;

public sealed class CancelLeaveRequestValidator : AbstractValidator<CancelLeaveRequestCommand>
{
    public CancelLeaveRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(LeaveLimits.CancellationReasonMaxLength);
}

public sealed class CancelLeaveRequestHandler(
    ICurrentUser currentUser,
    IDataScopeService dataScope,
    ILeaveRequestRepository requests,
    ILeaveLedgerRepository ledger,
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    LeaveBalanceService balances,
    IWorkflowService workflow,
    IIntegrationEventOutbox outbox,
    TimeProvider clock)
    : ICommandHandler<CancelLeaveRequestCommand>
{
    public async Task<Result> Handle(CancelLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsCancelApproved.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null || request.EmployeeId != command.EmployeeId)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        var wasApproved = request.Status == LeaveRequestStatus.Approved;
        var pendingApprovalId = request.Status == LeaveRequestStatus.Pending ? request.ApprovalRequestId : null;
        var cancelled = request.Cancel(command.Reason, currentUser.UserId, clock.GetUtcNow().UtcDateTime);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        if (pendingApprovalId is { } approvalId)
        {
            var workflowCancelled = await workflow.CancelAsync(approvalId, currentUser.EmployeeId ?? request.EmployeeId, cancellationToken);
            if (workflowCancelled.IsFailure)
            {
                return workflowCancelled;
            }
        }

        var reversals = LeaveCancellation.Reversals(request, await ledger.ListAsync(request.EmployeeId, null, cancellationToken), request.CancellationReason);
        if (reversals.Count > 0)
        {
            ledger.AddRange(reversals);
            if (await profiles.FindByEmployeeIdAsync(request.EmployeeId, cancellationToken) is { } profile
                && await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken) is { IsSuccess: true } set)
            {
                await balances.RecalculateAsync(profile, set.Value, cancellationToken, reversals);
            }
        }

        outbox.Enqueue(new LeaveCancelled(
            request.TenantId, request.Id, request.EmployeeId, request.LeaveTypeId, request.StartDate, request.EndDate, request.CalculatedDays, wasApproved));
        return Result.Success();
    }
}

public static class LeaveCancellation
{
    /// <summary>Reversal rows for the request's Usage rows that are not reversed yet (idempotent).</summary>
    public static IReadOnlyList<LeaveLedgerEntry> Reversals(LeaveRequest request, IEnumerable<LeaveLedgerEntry> employeeLedger, string? reason)
    {
        var rows = employeeLedger.Where(e => e.Source == LeaveLedgerSource.Request && e.SourceRefId == request.Id).ToList();
        if (rows.Any(e => e.Type == LeaveLedgerEntryType.Reversal))
        {
            return [];
        }

        return rows.Where(e => e.Type == LeaveLedgerEntryType.Usage).Select(usage => LeaveLedgerEntry.Reversal(usage, reason)).ToList();
    }
}

public sealed record LeaveApprovalStepDto(
    int StepNumber, Guid? ApproverEmployeeId, string? ApproverName, string? FallbackRoleCode, bool IsRequired, string Status, string? Comment);

public sealed record LeaveApprovalDto(string Status, int? CurrentStepNumber, IReadOnlyList<LeaveApprovalStepDto> Steps);

/// <summary>
/// Leave request detail: every <see cref="LeaveRequestDto"/> field plus the employee name and the approval chain
/// (omitted without approval). <see cref="RemainingBalance"/>/<see cref="TeamOverlap"/> mirror
/// <see cref="LeaveRequestPreviewDto"/>'s "İzin Talebi" modal fields ("Kalan İzin Bakiyesi"/"Çakışan İzinler") so an
/// approver reviewing an already-submitted request sees the same context the requester saw before submitting;
/// both are best-effort (null/empty on any lookup miss) and never fail the request.
/// </summary>
public sealed record LeaveRequestDetailDto(
    Guid Id, Guid EmployeeId, Guid LeaveTypeId, LeaveTypeCode LeaveTypeCode, DateOnly StartDate, DateOnly EndDate, decimal CalculatedDays,
    string? Description, LeaveRequestStatus Status, LeaveRequestSource Source, Guid? ApprovalRequestId, int RevisionNo, DateTime SubmittedAt, DateTime? DecidedAt,
    string? LeaveTypeName, Guid? PreviousRequestId, DateTime? CancelledAt, string? CancellationReason, string EmployeeName, LeaveApprovalDto? Approval,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null, HalfDayPeriod? HalfDayPeriod = null,
    decimal? RemainingBalance = null, TeamOverlapDto? TeamOverlap = null);

public sealed class LeaveRequestDetailBuilder(
    ITenantContext tenant,
    IEmployeeLeaveProfileRepository profiles,
    ILeaveRuleSetRepository ruleSets,
    IEmployeeDirectory employees,
    IEmployeeLookup employeeLookup,
    ILeaveRequestRepository requests,
    LeaveBalanceService balances,
    IApprovalRequestLookup approvals)
{
    private const string PendingStepStatus = "Pending";

    /// <summary>Display cap only, matches <see cref="LeaveRequestPreview"/>'s own cap - the caller still sees the true total in <c>Count</c>.</summary>
    private const int TeamOverlapMaxEmployees = 20;

    public async Task<LeaveRequestDetailDto> BuildAsync(LeaveRequest request, bool forRequester, CancellationToken cancellationToken)
    {
        var dto = (await LeaveRequestMapper.ToDtosAsync(request.EmployeeId, [request], profiles, ruleSets, cancellationToken))[0];
        var status = request.ApprovalRequestId is { } approvalId ? await approvals.GetStatusAsync(tenant.TenantId, approvalId, cancellationToken) : null;

        var contactIds = new HashSet<Guid> { request.EmployeeId };
        foreach (var step in status?.Steps ?? [])
        {
            if (step.ApproverEmployeeId is { } approverId)
            {
                contactIds.Add(approverId);
            }
        }

        var names = (await employees.GetContactsAsync(tenant.TenantId, contactIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        LeaveApprovalDto? approval = null;
        if (status is not null)
        {
            var hideUndecidedApprovers = forRequester && !status.ShowCurrentApproverToRequester;
            approval = new LeaveApprovalDto(
                CamelCase(status.Status),
                status.CurrentStepNumber,
                status.Steps.OrderBy(s => s.StepNumber).Select(s =>
                {
                    var hidden = hideUndecidedApprovers && s.Status == PendingStepStatus;
                    var approverId = hidden ? null : s.ApproverEmployeeId;
                    return new LeaveApprovalStepDto(
                        s.StepNumber, approverId, approverId is { } id ? names.GetValueOrDefault(id) : null, s.FallbackRoleCode, s.IsRequired, CamelCase(s.Status),
                        hidden ? null : s.Comment);
                }).ToList());
        }

        var remainingBalance = await ResolveRemainingBalanceAsync(request, cancellationToken);
        var teamOverlap = await ResolveTeamOverlapAsync(request, cancellationToken);

        return new LeaveRequestDetailDto(
            dto.Id, dto.EmployeeId, dto.LeaveTypeId, dto.LeaveTypeCode, dto.StartDate, dto.EndDate, dto.CalculatedDays, dto.Description, dto.Status, dto.Source,
            dto.ApprovalRequestId, dto.RevisionNo, dto.SubmittedAt, dto.DecidedAt, dto.LeaveTypeName, dto.PreviousRequestId, dto.CancelledAt, dto.CancellationReason,
            names.GetValueOrDefault(request.EmployeeId) ?? string.Empty, approval, dto.StartTime, dto.EndTime, dto.HalfDayPeriod,
            remainingBalance, teamOverlap);
    }

    /// <summary>"Kalan İzin Bakiyesi": the employee's current available balance for this request's leave type (PerYear types only, else null).</summary>
    private async Task<decimal?> ResolveRemainingBalanceAsync(LeaveRequest request, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindByEmployeeIdAsync(request.EmployeeId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var set = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
        if (set.IsFailure)
        {
            return null;
        }

        var projected = await balances.ProjectAsync(profile, set.Value, cancellationToken);
        return projected.FirstOrDefault(b => b.LeaveTypeId == request.LeaveTypeId)?.AvailableBalance;
    }

    /// <summary>
    /// "Çakışan İzinler": same org unit, active (Pending/Approved) leave overlapping [StartDate, EndDate], excluding
    /// the requester - mirrors <see cref="LeaveRequestPreview"/>'s pre-submit team overlap for consistency.
    /// </summary>
    private async Task<TeamOverlapDto?> ResolveTeamOverlapAsync(LeaveRequest request, CancellationToken cancellationToken)
    {
        var orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, request.EmployeeId, cancellationToken);
        if (orgUnitId is null)
        {
            return null;
        }

        var peers = (await employees.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, [orgUnitId.Value], cancellationToken))
            .Where(id => id != request.EmployeeId).ToList();
        if (peers.Count == 0)
        {
            return new TeamOverlapDto(0, []);
        }

        var overlapping = await requests.ListActiveInRangeAsync(request.StartDate, request.EndDate, peers, cancellationToken);
        if (overlapping.Count == 0)
        {
            return new TeamOverlapDto(0, []);
        }

        var overlapNames = (await employees.GetContactsAsync(tenant.TenantId, overlapping.Select(r => r.EmployeeId).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var shown = overlapping
            .OrderBy(r => r.StartDate)
            .Take(TeamOverlapMaxEmployees)
            .Select(r => new TeamOverlapEmployeeDto(r.EmployeeId, overlapNames.GetValueOrDefault(r.EmployeeId, string.Empty), r.StartDate, r.EndDate))
            .ToList();
        return new TeamOverlapDto(overlapping.Count, shown);
    }

    /// <summary>Workflow status strings ("InProgress") in the API's camelCase enum style ("inProgress").</summary>
    public static string CamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

/// <summary>Self-service request detail; another employee's request is reported as not found.</summary>
[RequiresPermission("leave.my.requests")]
public sealed record GetMyLeaveRequestQuery(Guid RequestId) : IQuery<LeaveRequestDetailDto>;

public sealed class GetMyLeaveRequestHandler(ICurrentUser currentUser, ILeaveRequestRepository requests, LeaveRequestDetailBuilder details)
    : IQueryHandler<GetMyLeaveRequestQuery, LeaveRequestDetailDto>
{
    public async Task<Result<LeaveRequestDetailDto>> Handle(GetMyLeaveRequestQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var request = await requests.FindByIdAsync(query.RequestId, cancellationToken);
        return request is null || request.EmployeeId != employeeId
            ? Error.NotFound(LeaveDomainErrors.RequestNotFound)
            : await details.BuildAsync(request, forRequester: true, cancellationToken);
    }
}

[RequiresPermission("leave.requests.read")]
public sealed record GetEmployeeLeaveRequestQuery(Guid EmployeeId, Guid RequestId) : IQuery<LeaveRequestDetailDto>;

public sealed class GetEmployeeLeaveRequestHandler(IDataScopeService dataScope, ILeaveRequestRepository requests, LeaveRequestDetailBuilder details)
    : IQueryHandler<GetEmployeeLeaveRequestQuery, LeaveRequestDetailDto>
{
    public async Task<Result<LeaveRequestDetailDto>> Handle(GetEmployeeLeaveRequestQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var request = await requests.FindByIdAsync(query.RequestId, cancellationToken);
        return request is null || request.EmployeeId != query.EmployeeId
            ? Error.NotFound(LeaveDomainErrors.RequestNotFound)
            : await details.BuildAsync(request, forRequester: false, cancellationToken);
    }
}
