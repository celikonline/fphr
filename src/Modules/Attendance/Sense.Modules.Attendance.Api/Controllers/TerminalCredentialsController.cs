using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Kimlik ekleme gövdesi. <see cref="ToString"/> değeri yazmaz.</summary>
public sealed record AddTerminalCredentialRequest(Guid EmployeeId, TerminalCredentialKind Kind, string? Value)
{
    public override string ToString() => $"{nameof(AddTerminalCredentialRequest)} {{ {nameof(EmployeeId)} = {EmployeeId}, {nameof(Kind)} = {Kind} }}";
}

/// <summary>
/// Çalışan terminal kimlikleri (zaman-devam Z5.4): listele (yalnız tür + durum + oluşturulma; değer/özet ASLA dönmez), ekle (değer yalnız
/// istekte, HMAC özetiyle saklanır; pepper yoksa 503), pasifleştir. Toplu tanımlama: DataTransfer şablonu <c>terminal-credentials</c>.
/// İzinler komutlarda: attendance.terminal.view / attendance.terminal.manage.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminal-credentials")]
[Authorize]
public sealed class TerminalCredentialsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TerminalCredentialDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeTerminalCredentialsQuery(employeeId), ct));

    /// <summary>Kimlik ekler; yeni (ya da aynı çalışanda zaten var olan) kaydın kimliğini döner. Başka çalışanda aktifse 409.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Add([FromBody] AddTerminalCredentialRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddEmployeeTerminalCredentialCommand(request.EmployeeId, request.Kind, request.Value), ct));

    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateEmployeeTerminalCredentialCommand(id), ct));
}
