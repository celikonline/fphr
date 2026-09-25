using System.Globalization;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Sense.Modules.Attendance.Api.TerminalCredentials;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Kimlik ekleme gövdesi. <see cref="ToString"/> değeri yazmaz.</summary>
public sealed record AddTerminalCredentialRequest(Guid EmployeeId, TerminalCredentialKind Kind, string? Value)
{
    public override string ToString() => $"{nameof(AddTerminalCredentialRequest)} {{ {nameof(EmployeeId)} = {EmployeeId}, {nameof(Kind)} = {Kind} }}";
}

/// <summary>
/// Çalışan terminal kimlikleri (zaman-devam Z5.4): listele (yalnız tür + durum + oluşturulma; değer/özet ASLA dönmez), ekle (değer yalnız
/// istekte, HMAC özetiyle saklanır; pepper yoksa 503), yeniden etkinleştir, pasifleştir. Toplu tanımlama: DataTransfer şablonu <c>terminal-credentials</c>.
/// İzinler komutlarda: attendance.terminal.view / attendance.terminal.manage. Ekleme ve etkinleştirme kullanıcı başına sınırlıdır
/// (<see cref="TerminalCredentialThrottle"/>): istek sınırı ve "PIN başkasında aktif" ret bütçesi aşılınca 429 (genel ileti, Retry-After).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminal-credentials")]
[Authorize]
public sealed class TerminalCredentialsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TerminalCredentialDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeTerminalCredentialsQuery(employeeId), ct));

    /// <summary>Kimlik ekler; yeni (ya da aynı çalışanda zaten var olan) kaydın kimliğini döner. Başka çalışanda aktifse 409.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Add([FromBody] AddTerminalCredentialRequest request, CancellationToken ct)
    {
        if (Throttled(checkPinBudget: request.Kind == TerminalCredentialKind.Pin) is { } tooMany)
        {
            return tooMany;
        }

        var result = await Dispatcher.Send(new AddEmployeeTerminalCredentialCommand(request.EmployeeId, request.Kind, request.Value), ct);
        CountPinReject(result);
        return FromResult(result);
    }

    /// <summary>Pasif kimliği aynı çalışanda yeniden etkinleştirir (özet değişmez). İşten ayrılmış çalışan → 422.</summary>
    [HttpPost(ApiRoutes.IdParam + "/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        // Tür istekte yok: bütçesi dolmuş kullanıcının etkinleştirmesi de durdurulur.
        if (Throttled(checkPinBudget: true) is { } tooMany)
        {
            return tooMany;
        }

        var result = await Dispatcher.Send(new ActivateEmployeeTerminalCredentialCommand(id), ct);
        CountPinReject(result);
        return FromResult(result);
    }

    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateEmployeeTerminalCredentialCommand(id), ct));

    private TerminalCredentialThrottle Throttle => HttpContext.RequestServices.GetRequiredService<TerminalCredentialThrottle>();

    private Guid UserId => HttpContext.RequestServices.GetRequiredService<ICurrentUser>().UserId ?? Guid.Empty;

    private IActionResult? Throttled(bool checkPinBudget)
    {
        if (!Throttle.TryAcquireRequest(UserId, out var retryAfter)
            || (checkPinBudget && Throttle.IsPinRejectBudgetExhausted(UserId, out retryAfter)))
        {
            Response.Headers[HeaderNames.RetryAfter] = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Problem(new Error(ErrorCodes.RateLimitExceeded, ErrorType.TooManyRequests));
        }

        return null;
    }

    private void CountPinReject(Result result)
    {
        if (result.IsFailure && result.Error.Code == TerminalCredentialErrors.Rejected)
        {
            Throttle.RegisterPinReject(UserId);
        }
    }
}
