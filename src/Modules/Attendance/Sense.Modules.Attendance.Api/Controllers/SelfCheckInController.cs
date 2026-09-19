using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record QrCheckInRequest(string Code, PunchDirection Direction);

public sealed record GpsCheckInRequest(double Latitude, double Longitude, Guid? LocationId, PunchDirection Direction);

/// <summary>
/// Self-servis QR/GPS check-in (S-31): mobil/web'den giriş-çıkış kaydı, PDKS CSV toplu içe alma dışındaki ek kanal.
/// Girdiği kayıt, PDKS import ile aynı AttendanceDay upsert'ini kullanır (attendance.my.checkin).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/self-checkin")]
[Authorize]
public sealed class SelfCheckInController : ApiControllerBase
{
    /// <summary>Lokasyon girişine asılan QR kodu taratarak giriş/çıkış (kabul kriteri 1).</summary>
    [HttpPost("qr")]
    [ProducesResponseType<SelfCheckInResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Qr([FromBody] QrCheckInRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new QrCheckInCommand(request.Code, request.Direction), ct));

    /// <summary>Cihaz GPS konumuyla giriş/çıkış, isteğe bağlı lokasyon + geofence doğrulaması (kabul kriteri 2).</summary>
    [HttpPost("gps")]
    [ProducesResponseType<SelfCheckInResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Gps([FromBody] GpsCheckInRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new GpsCheckInCommand(request.Latitude, request.Longitude, request.LocationId, request.Direction), ct));

    /// <summary>"Şu an içeride miyim" - self check-in sayfasının hangi butonu (Giriş/Çıkış) göstereceğini belirler.</summary>
    [HttpGet("status")]
    [ProducesResponseType<MyCheckInStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyCheckInStatusQuery(), ct));
}
