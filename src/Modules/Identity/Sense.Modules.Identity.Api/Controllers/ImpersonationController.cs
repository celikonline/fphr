using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Impersonation;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record RequestImpersonationRequest(Guid TenantId, string Reason);

public sealed record StartImpersonationRequest(Guid TenantId);

/// <summary>
/// Platform personelinin kiracı verisine süreli, onaylı erişimi. Talep/başlatma: identity.users.impersonate
/// (yalnız platform personeli, hiçbir kiracı-taraf sistem rolüne verilmez); onay/iptal: yalnız kiracı Owner/Admin.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Impersonation)]
[Authorize]
public sealed class ImpersonationController : ApiControllerBase
{
    /// <summary>Kiracının destek erişimi grantları (?status=Pending|Approved|Active|Expired|Revoked).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ImpersonationGrantDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListImpersonationGrantsQuery(status), ct));

    [HttpPost("request")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RequestGrant([FromBody] RequestImpersonationRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new RequestImpersonationCommand(request.TenantId, request.Reason), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error);
    }

    [HttpPost(ApiRoutes.IdParam + "/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ApproveImpersonationCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/start")]
    [ProducesResponseType<ImpersonationSessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(Guid id, [FromBody] StartImpersonationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new StartImpersonationCommand(request.TenantId, id), ct));

    [HttpPost(ApiRoutes.IdParam + "/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeImpersonationCommand(id), ct));
}
