using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Ledger;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

/// <summary>
/// senseik-048: "İzinlerim"/"Fazla mesailerim" yanındaki şirket geneli izin/fazla mesai defteri (İK Müdürü/Hesap
/// Sahibi - leave.company_ledger.read). Excel dışa aktarımı aynı verinin Raporlar kataloğundaki karşılığı olan
/// "leave.company_ledger" IReportProvider'ı üzerinden yürür (bkz. ReportsController).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/company/ledger")]
[Authorize]
public sealed class LeaveCompanyLedgerController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<CompanyLeaveLedgerDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] Guid? orgUnitId,
        [FromQuery] bool includeTerminated,
        [FromServices] IErrorLocalizer localizer,
        CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetCompanyLeaveLedgerQuery(year, month, orgUnitId, includeTerminated), ct)).Localize(localizer));
}
