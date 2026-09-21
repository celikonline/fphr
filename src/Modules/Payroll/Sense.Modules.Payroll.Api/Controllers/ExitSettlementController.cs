using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>
/// Employee card > "Çıkış hesabı": the kıdem/ihbar/izin amounts frozen when the employee was terminated.
/// Read-only; the record is written by the EmployeeTerminated integration event handler. Gated by payroll.severance.estimate.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/employees/{employeeId:guid}/exit-settlement")]
[Authorize]
public sealed class ExitSettlementController : ApiControllerBase
{
    /// <summary>The employee's most recent exit settlement; 404 when none is recorded.</summary>
    [HttpGet]
    [ProducesResponseType<ExitSettlementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetExitSettlementQuery(employeeId), ct));
}
