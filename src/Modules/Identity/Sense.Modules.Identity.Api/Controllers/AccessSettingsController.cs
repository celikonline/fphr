using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Identity.Api.Controllers;

public sealed record AccessSettingsRequest(
    bool ShowOrgInfo,
    bool ShowUpcomingLeaves,
    bool ShowUpcomingBirthdays,
    bool ShowEventCalendar,
    bool ShowFiles,
    bool ShowAssets,
    bool ShowWorkPhoneNumbers,
    bool ShowOrgChart,
    bool ShowCustomFields,
    bool ShowCalendarMenu,
    bool ShowAllLeavesInCalendar);

/// <summary>Ayarlar &gt; Sistem ayarları &gt; Erişim ayarları sayfası (Faz 0 madde 1): ana sayfa/hesap/takvim görünürlük anahtarları.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Settings + "/access")]
[Authorize]
public sealed class AccessSettingsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<AccessSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => FromResult(await Dispatcher.Query(new GetAccessSettingsQuery(), ct));

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update([FromBody] AccessSettingsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateAccessSettingsCommand(
            request.ShowOrgInfo, request.ShowUpcomingLeaves, request.ShowUpcomingBirthdays, request.ShowEventCalendar, request.ShowFiles, request.ShowAssets,
            request.ShowWorkPhoneNumbers, request.ShowOrgChart, request.ShowCustomFields,
            request.ShowCalendarMenu, request.ShowAllLeavesInCalendar), ct));
}

public sealed record SecurityPolicyRequest(IReadOnlyList<string> RequireMfaForRoles);

/// <summary>Ayarlar &gt; Sistem ayarları &gt; Güvenlik politikası sayfası (senseik-051): belirli roller için zorunlu 2FA.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route(IdentityRoutes.Settings + "/security")]
[Authorize]
public sealed class SecurityPolicyController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<SecurityPolicyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => FromResult(await Dispatcher.Query(new GetSecurityPolicyQuery(), ct));

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update([FromBody] SecurityPolicyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateSecurityPolicyCommand(request.RequireMfaForRoles), ct));
}
