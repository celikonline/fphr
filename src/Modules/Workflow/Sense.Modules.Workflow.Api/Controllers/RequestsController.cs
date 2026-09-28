using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Request to reassign the approver on one or more still-pending tasks of a request (TK-009).</summary>
public sealed record EditApproversRequest(IReadOnlyList<ApprovalStepApproverEdit> Approvers);

/// <summary>FG-009: body of a new comment on an approval request.</summary>
public sealed record AddCommentRequest(string? Text);

/// <summary>FG-009: id of the comment just added.</summary>
public sealed record CommentIdResponse(Guid Id);

/// <summary>Approval request status/history lookups.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/requests")]
[Authorize]
public sealed class RequestsController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>
    /// Status and step timeline of one approval request. No flat permission - the handler authorizes the caller as a
    /// party of the request (requester, approver/decider of any task, fallback role member, override holder); anyone
    /// else gets 404.
    /// </summary>
    [HttpGet(ApiRoutes.IdParam + "/status")]
    [ProducesResponseType<ApprovalRequestStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetApprovalStatusQuery(id), ct));

    /// <summary>FG-009: comments on one approval request, oldest first. Same party-only authorization as <see cref="Status"/>.</summary>
    [HttpGet(ApiRoutes.IdParam + "/comments")]
    [ProducesResponseType<IReadOnlyList<ApprovalRequestCommentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Comments(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListApprovalRequestCommentsQuery(id), ct));

    /// <summary>
    /// FG-009: adds a comment as the caller (party-only, 404 otherwise); the other parties get an in-app notification.
    /// A cancelled request takes no comments (400 workflow.comment.request_closed).
    /// </summary>
    [HttpPost(ApiRoutes.IdParam + "/comments")]
    [ProducesResponseType<CommentIdResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new AddApprovalRequestCommentCommand(id, CurrentUser.EmployeeId ?? Guid.Empty, request.Text), ct);
        return result.IsSuccess ? Ok(new CommentIdResponse(result.Value)) : Problem(result.Error);
    }

    /// <summary>
    /// Edits the approver on one or more of the request's still-pending tasks - a one-time, per-request adjustment
    /// (the source policy/template is untouched). No flat permission - the handler authorizes the caller as the
    /// requester or the requester's direct manager.
    /// </summary>
    [HttpPut(ApiRoutes.IdParam + "/approvers")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EditApprovers(Guid id, [FromBody] EditApproversRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new EditApprovalRequestApproversCommand(id, CurrentUser.EmployeeId ?? Guid.Empty, request.Approvers), ct));
}
