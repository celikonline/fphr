using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>"Okudum, onaylıyorum" isteği gövdesi (TK-030 kabul kriteri 2); kanal belirtilmezse web varsayılır.</summary>
public sealed record AcknowledgeMyPayslipRequest(PayslipDeliveryChannel Channel = PayslipDeliveryChannel.Web);

/// <summary>Tek bir bordro satırı (Payslip) üzerindeki uç noktalar - şimdilik yalnızca ücret pusulası indirme
/// (senseik-019 3a, İş K. md.37). Diğer Payslip işlemleri (onay/kilit gibi) P8+ kapsamı.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/payslips")]
[Authorize]
public sealed class PayslipsController : ApiControllerBase
{
    /// <summary>Ücret pusulasını düz metin olarak indirir (m.37 asgari içerik: kazançlar + tüm kesintiler ve sebepleri).</summary>
    [HttpGet("{payslipId:guid}/document")]
    public async Task<IActionResult> Document(Guid payslipId, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ExportPayslipDocumentQuery(payslipId), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    /// <summary>Every payslip of one employee, newest period first (employee card "Bordro" tab).</summary>
    [HttpGet("~/api/v{version:apiVersion}/payroll/employees/{employeeId:guid}/payslips")]
    [ProducesResponseType<IReadOnlyList<EmployeePayslipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForEmployee(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeePayslipsQuery(employeeId), ct));

    /// <summary>"Bordro Pusulam" (TK-030): çağıranın kendi ücret pusulaları, yalnız ödenmiş/kilitli dönemler.</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<MyPayslipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> My(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyPayslipsQuery(), ct));

    /// <summary>Kendi ücret pusulasının PDF'i (TK-030); başkasınınsa veya görünmeyen bir dönemdeyse 404.</summary>
    [HttpGet("my/{payslipId:guid}/document")]
    public async Task<IActionResult> MyDocument(Guid payslipId, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetMyPayslipDocumentQuery(payslipId), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    /// <summary>"Okudum, onaylıyorum" (TK-030 kabul kriteri 2); idempotent, ikinci çağrı da 204 döner.</summary>
    [HttpPost("my/{payslipId:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AcknowledgeMy(Guid payslipId, [FromBody] AcknowledgeMyPayslipRequest? request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AcknowledgeMyPayslipCommand(payslipId, request?.Channel ?? PayslipDeliveryChannel.Web), ct));
}
