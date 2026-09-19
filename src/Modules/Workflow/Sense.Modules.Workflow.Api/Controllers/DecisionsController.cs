using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Request to decide a task via the single-use link in the "you have a pending approval" email.</summary>
public sealed record DecideByTokenRequest(string Token, ApprovalOutcome Outcome, string? Comment);

/// <summary>Email-driven decisions. Kept [Authorize] at class level for consistency; the one action below is public.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/decisions")]
[Authorize]
public sealed class DecisionsController : ApiControllerBase
{
    /// <summary>Decides a task via the single-use link in the decision email. Public, unauthenticated.</summary>
    [HttpPost("by-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-attempt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DecideByToken([FromBody] DecideByTokenRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new DecideByEmailTokenCommand(
                request.Token, request.Outcome, request.Comment,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent),
            ct));
}
