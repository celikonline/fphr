using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Application.Adjustments;
using Sense.Modules.Payroll.Application.Closing;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain.Adjustments;
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
/// <summary>Body of adding a one-off taxable earning to an employee.</summary>
public sealed record AddPayrollAdjustmentRequest(Guid EmployeeId, PayrollAdjustmentKind Kind, string Label, decimal GrossAmount);

/// <summary>Body of completing a manual closing check.</summary>
public sealed record CompleteClosingCheckRequest(string? Note);

/// <summary>Body of waiving a closing check: why it may be skipped.</summary>
public sealed record WaiveClosingCheckRequest(string Reason);

/// <summary>Body of "Yeniden aç": why the period is reopened.</summary>
public sealed record ReopenPayrollPeriodRequest(string Reason);

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

    /// <summary>Downloads the period's APHB (SGK e-Bildirge) DRAFT file (senseik-019 3b - K-7 backlog karşılığı; bkz.
    /// AphbFileBuilder doc for the "not the official SGK schema" caveat).</summary>
    [HttpGet("{id:guid}/aphb")]
    public async Task<IActionResult> Aphb(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ExportAphbFileQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    /// <summary>Approves the period (Open → Approved); refused while changed payslips are not recalculated.</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ApprovePayrollPeriodCommand(id), ct));

    /// <summary>Marks the approved period as paid (Approved → Paid).</summary>
    [HttpPost("{id:guid}/mark-paid")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkPayrollPeriodPaidCommand(id), ct));

    /// <summary>Locks the paid period (Paid → Locked).</summary>
    [HttpPost("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LockPayrollPeriodCommand(id), ct));

    /// <summary>Reopens an approved, paid or locked period; a written reason is required and audited.</summary>
    [HttpPost("{id:guid}/reopen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] ReopenPayrollPeriodRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReopenPayrollPeriodCommand(id, request.Reason), ct));

    /// <summary>The period's closing checklist; a never-opened checklist shows the template with every item pending.</summary>
    [HttpGet("{id:guid}/closing-checks")]
    [ProducesResponseType<IReadOnlyList<ClosingCheckDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClosingChecks(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetClosingChecklistQuery(id), ct));

    /// <summary>Opens the checklist if needed and re-runs its automatic rules.</summary>
    [HttpPost("{id:guid}/closing-checks/run")]
    [ProducesResponseType<IReadOnlyList<ClosingCheckDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RunClosingChecks(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RunClosingChecksCommand(id), ct));

    /// <summary>Ticks a manual checklist item off (optional note).</summary>
    [HttpPost("{id:guid}/closing-checks/{code}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CompleteClosingCheck(Guid id, string code, [FromBody] CompleteClosingCheckRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CompleteClosingCheckCommand(id, code, request.Note), ct));

    /// <summary>Waives a checklist item with a written reason.</summary>
    [HttpPost("{id:guid}/closing-checks/{code}/waive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> WaiveClosingCheck(Guid id, string code, [FromBody] WaiveClosingCheckRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WaiveClosingCheckCommand(id, code, request.Reason), ct));

    /// <summary>The period against the previous month: totals and per-employee change; rows past the threshold (default 5%) come first.</summary>
    [HttpGet("{id:guid}/comparison")]
    [ProducesResponseType<PeriodComparisonDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Comparison(Guid id, [FromQuery] decimal? thresholdPercent, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetPeriodComparisonQuery(id, thresholdPercent), ct));

    /// <summary>Downloads the bank payment list (generic CSV) of an approved, paid or locked period; contains IBANs (payroll.bankfile.export).</summary>
    [HttpGet("{id:guid}/bank-file")]
    public async Task<IActionResult> BankFile(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ExportBankPaymentFileQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    /// <summary>Downloads the MUHSGK / SGK premium data set (per-employee premiums and taxes, with totals) as a CSV for reconciliation; not the official declaration format.</summary>
    [HttpGet("{id:guid}/declaration-summary")]
    public async Task<IActionResult> DeclarationSummary(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ExportDeclarationSummaryQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    /// <summary>The period's one-off taxable earnings (prim, komisyon...), all employees.</summary>
    [HttpGet("{id:guid}/adjustments")]
    [ProducesResponseType<IReadOnlyList<PayrollAdjustmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjustments(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPayrollAdjustmentsQuery(id), ct));

    /// <summary>Adds a gross one-off earning to an employee in an open period; returns its id.</summary>
    [HttpPost("{id:guid}/adjustments")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddAdjustment(Guid id, [FromBody] AddPayrollAdjustmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddPayrollAdjustmentCommand(id, request.EmployeeId, request.Kind, request.Label, request.GrossAmount), ct));

    /// <summary>Removes an earning while its period is still open.</summary>
    [HttpDelete("~/api/v{version:apiVersion}/payroll/adjustments/{adjustmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAdjustment(Guid adjustmentId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RemovePayrollAdjustmentCommand(adjustmentId), ct));
}
