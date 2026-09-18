using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Open-a-period request body.</summary>
public sealed record OpenPayrollPeriodRequest(int Year, int Month);

/// <summary>
/// AD Bordro > Dönemler (11.3.07 P3): open a tenant-month period, run/recalculate its payslips, list/export it, and
/// list the "Değişenler" (active dirty flags) for the future filter screen (P8, web is out of scope for that one).
/// Write actions and dirty-flags (open/run/recalculate-dirty/dirty-flags) require payroll.period.recalculate; the
/// read-only list/get/export actions require payroll.trace.view instead - both enforced on the commands/queries
/// themselves via [RequiresPermission].
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/periods")]
[Authorize]
public sealed class PayrollPeriodsController : ApiControllerBase
{
    /// <summary>Every period the tenant has opened, newest first (read-only "Dönem listesi" screen).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PayrollPeriodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPayrollPeriodsQuery(), ct));

    /// <summary>One period's header + its payslip lines (read-only "Dönem detayı" screen). 404 on another tenant's/unknown period. Optional costCenterId query parameter filters payslips to only the specified cost center.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PayrollPeriodDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, Guid? costCenterId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetPayrollPeriodQuery(id, costCenterId), ct));

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

    /// <summary>Downloads the period's payroll summary workbook (K-7: Excel özet, SGK/banka dosyası ilk sürüm dışı). Optional costCenterId query parameter filters the export to only the specified cost center.</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, Guid? costCenterId, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ExportPayrollPeriodQuery(id, costCenterId), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }
}
