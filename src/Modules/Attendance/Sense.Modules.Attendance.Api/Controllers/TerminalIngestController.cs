using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sense.Modules.Attendance.Api.Terminals;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Okutma isteği gövdesi: en çok <see cref="TerminalPunchIngest.MaxItemsPerRequest"/> kalem.</summary>
public sealed record TerminalPunchBatchRequest(IReadOnlyList<TerminalPunchItem?>? Items);

/// <summary>
/// Terminal okutma gönderme ucu (zaman-devam Z5.1). Kimlik YALNIZ <c>X-Terminal-Key</c> şemasıyla (JWT kabul edilmez; [AllowAnonymous]
/// değildir); kiracı doğrulanmış terminalden kurulur, gövdeden asla. Hız sınırı kimlik doğrulamadan önce (IP + anahtar).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminals")]
[Authorize(AuthenticationSchemes = TerminalKeyDefaults.AuthenticationScheme)]
[TerminalTenantScope]
[EnableRateLimiting(TerminalKeyDefaults.RateLimitPolicy)]
public sealed class TerminalIngestController(TerminalPunchIngest ingest) : ApiControllerBase
{
    /// <summary>Toplu, idempotent okutma: kalem bazında accepted/ignored/rejected (+neden) döner.</summary>
    [HttpPost("punches")]
    [RequestSizeLimit(TerminalKeyDefaults.MaxRequestBodyBytes)]
    [ProducesResponseType<TerminalPunchBatchResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Punches([FromBody] TerminalPunchBatchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TerminalPrincipal.Read(User) is { } terminal
            ? FromResult(await ingest.IngestAsync(terminal, request.Items, ct))
            : Unauthorized();
    }

    /// <summary>Nabız: son görülme zamanı güncellenir, sunucu saati döner.</summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType<TerminalHeartbeatDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Heartbeat(CancellationToken ct) =>
        TerminalPrincipal.Read(User) is { } terminal
            ? FromResult(await ingest.HeartbeatAsync(terminal, ct))
            : Unauthorized();
}
