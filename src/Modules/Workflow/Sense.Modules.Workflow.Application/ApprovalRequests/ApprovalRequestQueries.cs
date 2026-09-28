using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application.ApprovalRequests;

/// <summary>
/// Status/history of one approval request. No flat [RequiresPermission] - authorization is caller-identity-dependent
/// (requester, an assignee on any of its tasks, or a caller holding an inbox/override permission) so it is checked
/// inside the handler; anyone else gets NotFound, not Forbidden, so existence is not leaked.
/// </summary>
public sealed record GetApprovalStatusQuery(Guid ApprovalRequestId) : IQuery<ApprovalRequestStatusDto>;

public sealed class GetApprovalStatusHandler(
    ICurrentUser currentUser, IApprovalRequestRepository requests, IPermissionService permissions)
    : IQueryHandler<GetApprovalStatusQuery, ApprovalRequestStatusDto>
{
    public async Task<Result<ApprovalRequestStatusDto>> Handle(GetApprovalStatusQuery query, CancellationToken cancellationToken)
    {
        var loaded = await requests.GetByIdAsync(query.ApprovalRequestId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var request = loaded.Value;
        var callerEmployeeId = currentUser.EmployeeId;
        var authorized = (callerEmployeeId is not null && request.RequesterEmployeeId == callerEmployeeId)
            || (callerEmployeeId is not null && request.Tasks.Any(t => t.ApproverEmployeeId == callerEmployeeId));

        if (!authorized && currentUser.UserId is { } userId)
        {
            authorized = await permissions.HasAsync(userId, "workflow.request.override", cancellationToken)
                || await permissions.HasAsync(userId, "workflow.inbox.view", cancellationToken);
        }

        if (!authorized)
        {
            return Error.NotFound(WorkflowErrors.RequestNotFound);
        }

        Guid? currentApproverEmployeeId = request.ShowCurrentApproverToRequester ? request.CurrentTask?.ApproverEmployeeId : null;

        return new ApprovalRequestStatusDto(
            request.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId, request.Status.ToString(),
            request.StepCount, request.CurrentStepNumber, currentApproverEmployeeId);
    }
}

/// <summary>The caller's pending approval tasks - assigned directly, or via a fallback role queue the caller belongs to.</summary>
[RequiresPermission("workflow.inbox.view")]
public sealed record ListInboxQuery : IQuery<IReadOnlyList<InboxTaskDto>>;

public sealed class ListInboxHandler(
    ICurrentUser currentUser,
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IRoleAssignmentLookup roleAssignments,
    IEmployeeDirectory employeeDirectory,
    IEnumerable<IApprovalRequestSummaryProvider> summaryProviders) : IQueryHandler<ListInboxQuery, IReadOnlyList<InboxTaskDto>>
{
    /// <summary>
    /// The only fallback role queue IApproverResolver falls back to today is "HR" (TenantSettings.Workflow.FallbackApproverRoleCode
    /// defaults to SystemRoleCodes.HR); there is no "list my roles" port yet, so - matching that reality - this probes
    /// that one role code rather than enumerating every system role. Revisit if FallbackApproverRoleCode becomes configurable.
    /// </summary>
    private const string FallbackRoleCode = "HR";

    public async Task<Result<IReadOnlyList<InboxTaskDto>>> Handle(ListInboxQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Result.Success<IReadOnlyList<InboxTaskDto>>([]);
        }

        var fallbackRoleCodes = await roleAssignments.EmployeeHasRoleAsync(tenant.TenantId, employeeId, FallbackRoleCode, cancellationToken)
            ? new[] { FallbackRoleCode }
            : [];

        var tasks = await requests.ListInboxTasksAsync(employeeId, fallbackRoleCodes, cancellationToken);
        if (tasks.Count == 0)
        {
            return Result.Success<IReadOnlyList<InboxTaskDto>>([]);
        }

        // ApprovalTask doesn't carry RequestType/SubjectId/RequesterEmployeeId (those live on the parent aggregate) and
        // IApprovalRequestRepository has no bulk-by-ids read, so the parent requests are loaded one at a time.
        var requestsById = new Dictionary<Guid, ApprovalRequest>();
        foreach (var requestId in tasks.Select(t => t.ApprovalRequestId).Distinct())
        {
            var loaded = await requests.GetByIdAsync(requestId, cancellationToken);
            if (loaded.IsSuccess)
            {
                requestsById[requestId] = loaded.Value;
            }
        }

        var requesterIds = requestsById.Values.Select(r => r.RequesterEmployeeId).Distinct().ToList();
        var contacts = requesterIds.Count == 0
            ? []
            : await employeeDirectory.GetContactsAsync(tenant.TenantId, requesterIds, cancellationToken);
        var requesterNames = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var providersByRequestType = summaryProviders.ToDictionary(p => p.RequestType, StringComparer.Ordinal);
        var summariesByRequestId = new Dictionary<Guid, ApprovalRequestSummary?>();
        foreach (var parent in requestsById.Values)
        {
            summariesByRequestId[parent.Id] = providersByRequestType.TryGetValue(parent.RequestType, out var provider)
                ? await provider.GetSummaryAsync(tenant.TenantId, parent.SubjectId, cancellationToken)
                : null;
        }

        IReadOnlyList<InboxTaskDto> dtos = tasks
            .Where(t => requestsById.ContainsKey(t.ApprovalRequestId))
            .Select(t =>
            {
                var parent = requestsById[t.ApprovalRequestId];
                return new InboxTaskDto(
                    t.Id, parent.Id, parent.RequestType, parent.SubjectId, parent.RequesterEmployeeId,
                    requesterNames.GetValueOrDefault(parent.RequesterEmployeeId), t.StepNumber, t.ApproverEmployeeId is null,
                    parent.SubmittedAt, summariesByRequestId.GetValueOrDefault(parent.Id));
            })
            .ToList();

        return Result.Success(dtos);
    }
}

/// <summary>
/// FG-008: the tasks the caller decided (DecidedByEmployeeId == caller - direct, fallback-role and override decisions
/// alike), newest decision first. <see cref="From"/>/<see cref="To"/> are inclusive UTC decision days; <see cref="Type"/>
/// is an exact RequestType. Page/PageSize follow the list-endpoint defaults (1-based, 20, capped at 200).
/// </summary>
[RequiresPermission("workflow.inbox.view")]
public sealed record ListApprovalHistoryQuery(
    DateOnly? From, DateOnly? To, string? Type, int Page = PagingDefaults.FirstPage, int PageSize = PagingDefaults.DefaultPageSize)
    : IQuery<PagedResult<ApprovalHistoryItemDto>>;

public sealed class ListApprovalHistoryHandler(
    ICurrentUser currentUser,
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IEmployeeDirectory employeeDirectory,
    IEnumerable<IApprovalRequestSummaryProvider> summaryProviders) : IQueryHandler<ListApprovalHistoryQuery, PagedResult<ApprovalHistoryItemDto>>
{
    public async Task<Result<PagedResult<ApprovalHistoryItemDto>>> Handle(ListApprovalHistoryQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, PagingDefaults.FirstPage);
        var pageSize = query.PageSize < 1 ? PagingDefaults.DefaultPageSize : Math.Min(query.PageSize, PagingDefaults.MaxPageSize);
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return new PagedResult<ApprovalHistoryItemDto>([], page, pageSize, 0);
        }

        DateTime? fromUtc = query.From is { } from ? DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) : null;
        DateTime? toUtcExclusive = query.To is { } to && to < DateOnly.MaxValue
            ? DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            : null;
        var type = string.IsNullOrWhiteSpace(query.Type) ? null : query.Type.Trim();

        var decided = await requests.ListDecidedTasksAsync(
            employeeId, fromUtc, toUtcExclusive, type, (page - PagingDefaults.FirstPage) * pageSize, pageSize, cancellationToken);
        if (decided.Items.Count == 0)
        {
            return new PagedResult<ApprovalHistoryItemDto>([], page, pageSize, decided.TotalCount);
        }

        var requesterIds = decided.Items.Select(r => r.RequesterEmployeeId).Distinct().ToList();
        var contacts = await employeeDirectory.GetContactsAsync(tenant.TenantId, requesterIds, cancellationToken);
        var requesterNames = contacts.ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        var providersByRequestType = summaryProviders.ToDictionary(p => p.RequestType, StringComparer.Ordinal);
        var summariesByRequestId = new Dictionary<Guid, ApprovalRequestSummary?>();
        foreach (var row in decided.Items.DistinctBy(r => r.ApprovalRequestId))
        {
            summariesByRequestId[row.ApprovalRequestId] = providersByRequestType.TryGetValue(row.RequestType, out var provider)
                ? await provider.GetSummaryAsync(tenant.TenantId, row.SubjectId, cancellationToken)
                : null;
        }

        IReadOnlyList<ApprovalHistoryItemDto> items = decided.Items
            .Select(r => new ApprovalHistoryItemDto(
                r.TaskId, r.ApprovalRequestId, r.RequestType, r.SubjectId, r.RequesterEmployeeId,
                requesterNames.GetValueOrDefault(r.RequesterEmployeeId), r.StepNumber,
                r.TaskStatus == ApprovalTaskStatus.Approved ? ApprovalOutcome.Approved : ApprovalOutcome.Rejected,
                r.DecidedAt, r.Comment, r.RequestStatus, summariesByRequestId.GetValueOrDefault(r.ApprovalRequestId)))
            .ToList();

        return new PagedResult<ApprovalHistoryItemDto>(items, page, pageSize, decided.TotalCount);
    }
}

/// <summary>The caller's own submitted approval requests.</summary>
[RequiresPermission("workflow.my.requests")]
public sealed record ListMyApprovalRequestsQuery : IQuery<IReadOnlyList<MyApprovalRequestDto>>;

public sealed class ListMyApprovalRequestsHandler(ICurrentUser currentUser, IApprovalRequestRepository requests)
    : IQueryHandler<ListMyApprovalRequestsQuery, IReadOnlyList<MyApprovalRequestDto>>
{
    public async Task<Result<IReadOnlyList<MyApprovalRequestDto>>> Handle(ListMyApprovalRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Result.Success<IReadOnlyList<MyApprovalRequestDto>>([]);
        }

        var list = await requests.ListByRequesterAsync(employeeId, cancellationToken);
        IReadOnlyList<MyApprovalRequestDto> dtos = list
            .Select(r => new MyApprovalRequestDto(r.Id, r.RequestType, r.SubjectId, r.Status.ToString(), r.StepCount, r.CurrentStepNumber, r.SubmittedAt, r.CompletedAt))
            .ToList();

        return Result.Success(dtos);
    }
}
