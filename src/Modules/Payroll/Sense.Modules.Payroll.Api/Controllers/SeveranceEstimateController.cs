using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>
/// Employee card > İşten ayrılış sihirbazı (11.3.03 E5a) > read-only özet blok (senseik-011, F-06): kıdem/ihbar/
/// kullanılmamış izin ücreti tahmini. API only, gated by payroll.severance.estimate - nothing is persisted, the
/// wizard has not submitted a termination yet.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/employees/{employeeId:guid}/severance-estimate")]
[Authorize]
public sealed class SeveranceEstimateController : ApiControllerBase
{
    /// <summary><paramref name="terminationDate"/> and <paramref name="causeCategory"/> are the wizard's current (unsaved) picks.</summary>
    [HttpGet]
    [ProducesResponseType<SeveranceEstimateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid employeeId, [FromQuery] DateOnly terminationDate, [FromQuery] SeveranceCauseCategory causeCategory, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetSeveranceEstimateQuery(employeeId, terminationDate, causeCategory), ct));
}
