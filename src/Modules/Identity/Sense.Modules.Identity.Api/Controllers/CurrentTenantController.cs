using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record UpdateTenantProfileRequest(string Name, string? TaxNumber, string Locale, string TimeZone);

/// <summary>Çağıranın kendi şirketi (kiracısı): kurulum sihirbazının şirket bilgileri adımı ve "kurulumu tamamla" eylemi.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Tenants + "/current")]
[Authorize]
public sealed class CurrentTenantController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => FromResult(await Dispatcher.Query(new GetTenantProfileQuery(), ct));

    [HttpPut]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] UpdateTenantProfileRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateTenantProfileCommand(request.Name, request.TaxNumber, request.Locale, request.TimeZone), ct));

    /// <summary>Kurulum sihirbazını tamamlandı işaretler (idempotent).</summary>
    [HttpPost("setup/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CompleteSetup(CancellationToken ct) => FromResult(await Dispatcher.Send(new CompleteTenantSetupCommand(), ct));
}
