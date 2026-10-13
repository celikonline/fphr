using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>
/// Organizasyon/Şirket profil ekranı > read-only "aktif teşvik türleri" özeti (senseik-010/F-03). API only - the
/// data stays employee-scoped (EmployeePayrollProfile.IncentiveLawCode), the Company profile screen only displays
/// this summary, per docs/handoffs/employee-sicil-uyruk.md §3.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/incentives")]
[Authorize]
public sealed class IncentivesController : ApiControllerBase
{
    /// <summary>Every SGK teşvik kanunu kodu active for at least one employee on <paramref name="asOf"/> (defaults to today), with employee counts.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<IReadOnlyList<IncentiveSummaryItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary([FromQuery] DateOnly? asOf, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetActiveIncentiveSummaryQuery(asOf), ct));

    /// <summary>Migration preview (payroll.incentive.manage): the profile incentive codes in use, normalized ("5510" becomes "05510"), with the matching definition.</summary>
    [HttpGet("legacy-profile-codes")]
    [ProducesResponseType<IReadOnlyList<LegacyIncentiveCodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LegacyProfileCodes([FromQuery] DateOnly? asOf, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListLegacyIncentiveCodesQuery(asOf), ct));

    /// <summary>Creates assignments from a profile code; employees with a conflicting SGK assignment are skipped. The profile code is not touched.</summary>
    [HttpPost("legacy-profile-codes/convert")]
    [ProducesResponseType<ConvertLegacyIncentiveCodeResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConvertLegacyProfileCode([FromBody] ConvertLegacyIncentiveCodeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new ConvertLegacyIncentiveCodeCommand(request.Code, request.IncentiveDefinitionId, request.FromYear, request.FromMonth, request.ToYear, request.ToMonth), ct));
}

public sealed record ConvertLegacyIncentiveCodeRequest(string Code, Guid IncentiveDefinitionId, int FromYear, int FromMonth, int? ToYear, int? ToMonth);
