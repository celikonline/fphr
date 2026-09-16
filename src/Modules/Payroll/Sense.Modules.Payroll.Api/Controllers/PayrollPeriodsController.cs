using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Open-a-period request body.</summary>
public sealed record OpenPayrollPeriodRequest(int Year, int Month);

/// <summary>
/// AD Bordro > Dönemler (11.3.07 P3): open a tenant-month period, run/recalculate its payslips, and list the
/// "Değişenler" (active dirty flags) for the future filter screen (P8, web is out of scope here). All actions require
/// payroll.period.recalculate (enforced on the commands/queries themselves via [RequiresPermission]).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/periods")]
[Authorize]
public sealed class PayrollPeriodsController : ApiControllerBase
{
    /// <summary>Opens a new period ("Dönem aç", akış §2).</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Open([FromBody] OpenPayrollPeriodRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new OpenPayrollPeriodCommand(request.Year, request.Month), ct));

    /// <summary>Recalculates every active employee's payslip in the period (initial run, and later "Tümünü yeniden hesapla").</summary>
    [HttpPost("{id:guid}/run")]
    [ProducesResponseType<PayrollPeriodRunSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecalculateAllPayslipsCommand(id), ct));

    /// <summary>Recalculates only the employees with an active dirty flag ("Değişenleri yeniden hesapla").</summary>
    [HttpPost("{id:guid}/recalculate-dirty")]
    [ProducesResponseType<PayrollPeriodRunSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecalculateDirty(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecalculateDirtyPayslipsCommand(id), ct));

    /// <summary>Active "Değişti" flags for the period ("Değişenler" filter, P8).</summary>
    [HttpGet("{id:guid}/dirty-flags")]
    [ProducesResponseType<IReadOnlyList<PayslipDirtyFlagDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DirtyFlags(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListDirtyFlagsQuery(id), ct));
}
