using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Request to decide (approve/reject) a pending approval task.</summary>
public sealed record DecideTaskRequest(ApprovalOutcome? Outcome, string? Comment);

/// <summary>The caller's approval inbox: pending tasks assigned directly, or via a fallback role queue, and decisions on them.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/inbox")]
[Authorize]
public sealed class InboxController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>The caller's pending approval tasks - assigned directly, or via a fallback role queue the caller belongs to.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InboxTaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListInboxQuery(), ct));

    /// <summary>Approves/rejects a pending task the caller is the assignee of (directly, or via a matching fallback role queue).</summary>
    [HttpPost("{taskId:guid}/decide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Decide(Guid taskId, [FromBody] DecideTaskRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new DecideApprovalTaskCommand(
                taskId, CurrentUser.EmployeeId ?? Guid.Empty, request.Outcome, request.Comment, ApprovalChannel.Web,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent),
            ct));

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
