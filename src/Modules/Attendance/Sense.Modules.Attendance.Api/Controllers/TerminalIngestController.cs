using System.Globalization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Api.Terminals;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Okutma isteği gövdesi: en çok <see cref="TerminalPunchIngest.MaxItemsPerRequest"/> kalem.</summary>
public sealed record TerminalPunchBatchRequest(IReadOnlyList<TerminalPunchItem?>? Items);

/// <summary>
/// Terminal okutma gönderme ucu (zaman-devam Z5.1). Kimlik YALNIZ <c>X-Terminal-Key</c> şemasıyla (JWT kabul edilmez; [AllowAnonymous]
/// değildir); kiracı doğrulanmış terminalden kurulur, gövdeden asla. Sınırlar: başarısız anahtar denemesi IP başına (şemada), doğrulanmış
/// terminal başına istek bütçesi burada (okutma <c>ceil(kalem/50)</c> izin, nabız 1) - aşılınca 429 + Retry-After.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminals")]
[Authorize(AuthenticationSchemes = TerminalKeyDefaults.AuthenticationScheme)]
[TerminalTenantScope]
public sealed class TerminalIngestController(TerminalPunchIngest ingest, TerminalRequestThrottle throttle) : ApiControllerBase
{
    /// <summary>Toplu, idempotent okutma: kalem bazında accepted/ignored/rejected (+neden) döner.</summary>
    [HttpPost("punches")]
    [RequestSizeLimit(TerminalKeyDefaults.MaxRequestBodyBytes)]
    [ProducesResponseType<TerminalPunchBatchResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Punches([FromBody] TerminalPunchBatchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (TerminalPrincipal.Read(User) is not { } terminal)
        {
            return Unauthorized();
        }

        return Throttled(terminal, TerminalRequestThrottle.CostOf(request.Items?.Count ?? 0))
            ?? FromResult(await ingest.IngestAsync(terminal, request.Items, ct));
    }

    /// <summary>Nabız: son görülme zamanı güncellenir, sunucu saati döner.</summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType<TerminalHeartbeatDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Heartbeat(CancellationToken ct)
    {
        if (TerminalPrincipal.Read(User) is not { } terminal)
        {
            return Unauthorized();
        }

        return Throttled(terminal, permits: 1) ?? FromResult(await ingest.HeartbeatAsync(terminal, ct));
    }

    /// <summary>Terminal bütçesi dolduysa 429 ProblemDetails (+Retry-After), değilse null.</summary>
    private IActionResult? Throttled(TerminalIdentity terminal, int permits)
    {
        if (throttle.TryAcquire(terminal.TerminalId, permits, out var retryAfter))
        {
            return null;
        }

        if (retryAfter is { } wait)
        {
            Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return Problem(new Error(ErrorCodes.RateLimitExceeded, ErrorType.TooManyRequests));
    }
}
