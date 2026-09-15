using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record GrantUnitAuthorizationRequest(Guid OrgUnitId, Guid UserId, bool CanViewSalaryAndPayments, bool CanViewUnitReports);

/// <summary>
/// Birim yetkilendirme (Faz 0 madde 3): departman ağacı satır üç nokta menüsü "Birim yetkilendirme".
/// Organization modülünün birim ağacı ekranı bu uçları çağırır; OrgUnitId yalnız bir Guid referansıdır
/// (Organization.Contracts.IOrgUnitLookup ile doğrulanır).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.UnitAuthorizations)]
[Authorize]
public sealed class UnitAuthorizationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UnitAuthorizationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListUnitAuthorizationsQuery(orgUnitId), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Grant([FromBody] GrantUnitAuthorizationRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new GrantUnitAuthorizationCommand(request.OrgUnitId, request.UserId, request.CanViewSalaryAndPayments, request.CanViewUnitReports), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error);
    }

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeUnitAuthorizationCommand(id), ct));
}
