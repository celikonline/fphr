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

/// <summary>Approval request status/history lookups.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/requests")]
[Authorize]
public sealed class RequestsController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>
    /// Status/history of one approval request. No flat permission - the handler authorizes the caller as the
    /// requester, an assignee on any of its tasks, or a caller holding an inbox/override permission.
    /// </summary>
    [HttpGet(ApiRoutes.IdParam + "/status")]
    [ProducesResponseType<ApprovalRequestStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetApprovalStatusQuery(id), ct));

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
