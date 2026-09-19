using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}
