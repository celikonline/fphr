using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.Companies;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to make sure the caller's tenant has its Organization defaults (unit types, root company, TR holiday calendar).</summary>
public sealed record EnsureOrganizationRequest(string CompanyName);

/// <summary>
/// Kurulum sihirbazı: yeni kiracının Organization varsayılanlarını (Worker'ın <c>TenantProvisioned</c> olayını işlemesini
/// beklemeden) senkron ve idempotent olarak hazırlar.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/bootstrap")]
[Authorize]
public sealed class OrganizationBootstrapController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Ensure([FromBody] EnsureOrganizationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new EnsureTenantOrganizationCommand(request.CompanyName), ct));
}
