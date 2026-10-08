using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record CommercialProvisionRequest(string CompanyName, string Slug, string OwnerEmail,
    string OwnerFirstName, string OwnerLastName, string? Phone, CommercialLicense License);

/// <summary>Sales integration uses a platform JWT. No anonymous tenant creation or entitlement changes.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(ApiRoutes.VersionedBase + "/identity/commercial")]
[Authorize]
public sealed class CommercialController : ApiControllerBase
{
    [HttpPost("provision")]
    public async Task<IActionResult> Provision([FromBody] CommercialProvisionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateTenantCommand(request.CompanyName, request.Slug, request.OwnerEmail,
            request.OwnerFirstName, request.OwnerLastName, TenantOwnerMode.Invite, null, request.Phone, "tr", request.License), ct));

    [HttpPost("{id:guid}/license")]
    public async Task<IActionResult> SetLicense(Guid id, [FromBody] CommercialLicense license, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetCommercialLicenseCommand(id, license), ct));

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyCommercialLicenseQuery(), ct));
}
