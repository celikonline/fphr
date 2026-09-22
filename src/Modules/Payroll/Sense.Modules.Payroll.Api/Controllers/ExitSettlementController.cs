using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Body of a manual exit-settlement line; a tax flag left null takes the default for the kind.</summary>
public sealed record AddExitSettlementLineRequest(ExitPayKind Kind, string Label, decimal GrossAmount, bool? SgkSubject, bool? IncomeTaxSubject, bool? StampTaxSubject);

/// <summary>Body of an edit of one line (also the automatic ones).</summary>
public sealed record UpdateExitSettlementLineRequest(string Label, decimal GrossAmount, bool SgkSubject, bool IncomeTaxSubject, bool StampTaxSubject);

/// <summary>
/// Employee card > "Çıkış hesabı": the kıdem/ihbar/izin amounts frozen when the employee was terminated.
/// The record is written by the EmployeeTerminated integration event handler; reading needs payroll.severance.estimate, changing its
/// lines needs payroll.exitsettlement.manage (both enforced by the query/command attributes).
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

    /// <summary>Adds a manual line (ikale bedeli, ek ödeme, çıkış ayı ücreti...) and renets; returns the new line's id.</summary>
    [HttpPost("~/api/v{version:apiVersion}/payroll/exit-settlements/{settlementId:guid}/lines")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddLine(Guid settlementId, [FromBody] AddExitSettlementLineRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddExitSettlementLineCommand(settlementId, request.Kind, request.Label, request.GrossAmount, request.SgkSubject, request.IncomeTaxSubject, request.StampTaxSubject), ct));

    /// <summary>Edits a line's name, amount and tax flags and renets.</summary>
    [HttpPut("~/api/v{version:apiVersion}/payroll/exit-settlements/{settlementId:guid}/lines/{lineId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateLine(Guid settlementId, Guid lineId, [FromBody] UpdateExitSettlementLineRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateExitSettlementLineCommand(settlementId, lineId, request.Label, request.GrossAmount, request.SgkSubject, request.IncomeTaxSubject, request.StampTaxSubject), ct));

    /// <summary>Removes a manual line and renets (the automatic lines cannot be removed).</summary>
    [HttpDelete("~/api/v{version:apiVersion}/payroll/exit-settlements/{settlementId:guid}/lines/{lineId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveLine(Guid settlementId, Guid lineId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RemoveExitSettlementLineCommand(settlementId, lineId), ct));

    /// <summary>Renets the settlement again.</summary>
    [HttpPost("~/api/v{version:apiVersion}/payroll/exit-settlements/{settlementId:guid}/recalculate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Recalculate(Guid settlementId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecalculateExitSettlementCommand(settlementId), ct));
}
