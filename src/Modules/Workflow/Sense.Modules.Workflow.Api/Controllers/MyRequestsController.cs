using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>The caller's own submitted approval requests.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/my-requests")]
[Authorize]
public sealed class MyRequestsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MyApprovalRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyApprovalRequestsQuery(), ct));
}
