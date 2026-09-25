using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Payslips;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Body of "İtiraz et": why the employee objects to the payslip.</summary>
public sealed record ObjectToPayslipRequest(string Reason);

/// <summary>
/// E2 self-service "Bordrolarım": the caller's own published payslips (period paid), the pusula PDF, "Onayla" (K4:
/// server time + IP + User-Agent) and "İtiraz et" (Workflow "PayslipObjection"). All actions require
/// payroll.my.payslip and only ever reach the caller's own payslips - others' ids answer 404.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/me/payslips")]
[Authorize]
public sealed class MyPayslipsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MyPayslipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyPayslipsQuery(), ct));

    /// <summary>The ücret pusulası PDF of one of the caller's published payslips.</summary>
    [HttpGet(ApiRoutes.IdParam + "/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new ExportMyPayslipDocumentQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    [HttpPost(ApiRoutes.IdParam + "/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AcknowledgeMyPayslipCommand(id, UserAgent()), ct));

    /// <summary>Files an objection; returns the id of the started approval request.</summary>
    [HttpPost(ApiRoutes.IdParam + "/object")]
    [ProducesResponseType<PayslipObjectionSubmittedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FileObjection(Guid id, [FromBody] ObjectToPayslipRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ObjectToMyPayslipCommand(id, request.Reason, UserAgent()), ct));

    private string? UserAgent()
    {
        var value = Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
