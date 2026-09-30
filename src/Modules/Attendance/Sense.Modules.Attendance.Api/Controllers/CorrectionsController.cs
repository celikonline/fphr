using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Corrections;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record SubmitPunchCorrectionRequest(DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut, string? Reason);

/// <summary>
/// Eksik giriş/çıkış düzeltme talebi (FG-004, attendance.my.corrections): the employee asks for a check-in/out time on a day;
/// a Workflow "PunchCorrection" approval decides it and, once approved, the day is updated and recomputed.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/corrections")]
[Authorize]
public sealed class CorrectionsController : ApiControllerBase
{
    [HttpPost("me")]
    [ProducesResponseType<PunchCorrectionRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitPunchCorrectionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitMyPunchCorrectionCommand(request.Date, request.CheckIn, request.CheckOut, request.Reason), ct));

    /// <summary>The caller's requests, newest first; <paramref name="from"/>/<paramref name="to"/> optionally limit the day range.</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<PunchCorrectionRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyPunchCorrectionsQuery(from, to), ct));

    [HttpPost("me/{id:guid}/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawMyPunchCorrectionCommand(id), ct));
}
