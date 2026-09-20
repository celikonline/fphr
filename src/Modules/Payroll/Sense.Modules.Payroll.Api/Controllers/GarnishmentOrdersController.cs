using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Garnishments;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Yeni bir icra/nafaka kesinti emri açma isteği gövdesi.</summary>
public sealed record CreateGarnishmentOrderRequest(GarnishmentType Type, string CaseReference, decimal MonthlyInstallmentAmount, decimal RemainingBalance, DateOnly StartDate);

/// <summary>Bir kesinti emrini kapatma isteği gövdesi.</summary>
public sealed record CloseGarnishmentOrderRequest(DateOnly ClosedDate);

/// <summary>
/// Çalışan kartı > Kesintiler (senseik-019 2c, İİK m.83): icra/nafaka kesinti emirlerini açma/listeleme/kapatma.
/// API only bu sürümde - web ekranı P8. Yazma işlemleri payroll.courtorder.manage, okuma payroll.trace.view gerektirir
/// (komut/sorgu üzerinde [RequiresPermission] ile uygulanır, EmployeePayrollProfilesController deseniyle aynı).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/employees/{employeeId:guid}/garnishment-orders")]
[Authorize]
public sealed class GarnishmentOrdersController : ApiControllerBase
{
    /// <summary>Çalışanın kapalılar dahil tüm kesinti emirleri, en yeni önce.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GarnishmentOrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListGarnishmentOrdersQuery(employeeId), ct));

    /// <summary>Yeni bir kesinti emri açar.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid employeeId, [FromBody] CreateGarnishmentOrderRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateGarnishmentOrderCommand(employeeId, request.Type, request.CaseReference, request.MonthlyInstallmentAmount, request.RemainingBalance, request.StartDate), ct));

    /// <summary>Bir kesinti emrini manuel kapatır.</summary>
    [HttpPost("{orderId:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid employeeId, Guid orderId, [FromBody] CloseGarnishmentOrderRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloseGarnishmentOrderCommand(orderId, request.ClosedDate), ct));
}
