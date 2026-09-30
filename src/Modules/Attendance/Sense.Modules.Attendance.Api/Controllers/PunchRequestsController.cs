using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.PunchRequests;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record CreatePunchRequestRequest(DateOnly WorkDate, DateTime OccurredAtUtc, PunchDirection Direction, string Reason);

/// <summary>
/// Eksik kayıt talebi (Z2.1): çalışan unuttuğu giriş/çıkışı gerekçeyle ister, "AttendancePunch" onay akışıyla karara bağlanır.
/// Çalışan uçları attendance.my.punchrequest, yönetici listesi attendance.punchrequest.manage gerektirir.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/punch-requests")]
[Authorize]
public sealed class PunchRequestsController : ApiControllerBase
{
    /// <summary>Kendi adıma eksik kayıt talebi oluşturur ve onay sürecini başlatır.</summary>
    [HttpPost("me")]
    [ProducesResponseType<PunchRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePunchRequestRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreatePunchRequestCommand(request.WorkDate, request.OccurredAtUtc, request.Direction, request.Reason), ct));

    /// <summary>Kendi taleplerim.</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<PunchRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyPunchRequestsQuery(), ct));

    /// <summary>Bekleyen kendi talebimi iptal eder.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CancelPunchRequestCommand(id), ct));

    /// <summary>Yönetici/İK listesi (veri kapsamı uygulanır); status verilmezse tümü.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PunchRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PunchRequestStatus? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPunchRequestsQuery(status), ct));
}
