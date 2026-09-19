using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

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
}
