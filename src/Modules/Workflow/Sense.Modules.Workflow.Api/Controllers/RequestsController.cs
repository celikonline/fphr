using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Approval request status/history lookups.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/requests")]
[Authorize]
public sealed class RequestsController : ApiControllerBase
{
    /// <summary>
    /// Status/history of one approval request. No flat permission - the handler authorizes the caller as the
    /// requester, an assignee on any of its tasks, or a caller holding an inbox/override permission.
    /// </summary>
    [HttpGet(ApiRoutes.IdParam + "/status")]
    [ProducesResponseType<ApprovalRequestStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetApprovalStatusQuery(id), ct));
}
