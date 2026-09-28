using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Request to decide (approve/reject) a pending approval task.</summary>
public sealed record DecideTaskRequest(ApprovalOutcome? Outcome, string? Comment);

/// <summary>FG-008: request to decide several pending tasks with one outcome (and, for a rejection, one shared comment).</summary>
public sealed record DecideBulkRequest(IReadOnlyList<Guid>? TaskIds, ApprovalOutcome? Outcome, string? Comment);

/// <summary>One task's decide-bulk outcome; ErrorCode/ErrorMessage (localized) are set only when Succeeded is false.</summary>
public sealed record DecideBulkItemResponse(Guid TaskId, bool Succeeded, string? ErrorCode, string? ErrorMessage);

/// <summary>FG-008 decide-bulk answer: one entry per distinct requested task id, in request order.</summary>
public sealed record DecideBulkResponse(IReadOnlyList<DecideBulkItemResponse> Results);

/// <summary>FG-008: the decision channel of an inbox call - <c>X-Client-App: mobile</c> is Mobile, anything else (none, "web") Web.</summary>
public static class ApprovalClientChannel
{
    public const string MobileClientApp = "mobile";

    public static ApprovalChannel From(string? clientApp) =>
        string.Equals(clientApp?.Trim(), MobileClientApp, StringComparison.OrdinalIgnoreCase) ? ApprovalChannel.Mobile : ApprovalChannel.Web;
}

/// <summary>The caller's approval inbox: pending tasks assigned directly, or via a fallback role queue, and decisions on them.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/inbox")]
[Authorize]
public sealed class InboxController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    private ApprovalChannel ClientChannel => ApprovalClientChannel.From(Request.Headers[SenseHeaderNames.ClientApp].ToString());

    /// <summary>The caller's pending approval tasks - assigned directly, or via a fallback role queue the caller belongs to.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InboxTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListInboxQuery(), ct));

    /// <summary>FG-008: the tasks the caller decided, newest first (from/to = inclusive UTC decision days, type = RequestType).</summary>
    [HttpGet("history")]
    [ProducesResponseType<PagedResult<ApprovalHistoryItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? type,
        [FromQuery] int page = PagingDefaults.FirstPage, [FromQuery] int pageSize = PagingDefaults.DefaultPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListApprovalHistoryQuery(from, to, type, page, pageSize), ct));

    /// <summary>Approves/rejects a pending task the caller is the assignee of (directly, or via a matching fallback role queue).</summary>
    [HttpPost("{taskId:guid}/decide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Decide(Guid taskId, [FromBody] DecideTaskRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new DecideApprovalTaskCommand(
                taskId, CurrentUser.EmployeeId ?? Guid.Empty, request.Outcome, request.Comment, ClientChannel,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent),
            ct));

    /// <summary>
    /// FG-008: decides up to 50 of the caller's pending tasks with one outcome. Each task is decided on its own (own
    /// transaction) and reported per task; an empty/oversized list or a rejection without a comment fails the call (400).
    /// </summary>
    [HttpPost("decide-bulk")]
    [ProducesResponseType<DecideBulkResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DecideBulk([FromBody] DecideBulkRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new DecideApprovalTasksBulkCommand(
                request.TaskIds, CurrentUser.EmployeeId ?? Guid.Empty, request.Outcome, request.Comment, ClientChannel,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent),
            ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        return Ok(new DecideBulkResponse(result.Value
            .Select(r => new DecideBulkItemResponse(r.TaskId, r.Succeeded, r.Error?.Code, r.Error is null ? null : Localizer.Message(r.Error)))
            .ToList()));
    }

    /// <summary>Decides a pending task on someone else's behalf (Admin override) - skips the assignee/fallback-role check.</summary>
    [HttpPost("{taskId:guid}/override-decide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> OverrideDecide(Guid taskId, [FromBody] DecideTaskRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new OverrideDecideApprovalTaskCommand(
                taskId, CurrentUser.EmployeeId ?? Guid.Empty, request.Outcome, request.Comment, ApprovalChannel.Web,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent),
            ct));
}
