using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record SetTerminalActiveRequest(bool IsActive);

public sealed record TerminalGroupRequest(string Name);

/// <summary>
/// Terminal yönetimi (zaman-devam Z5.1): CRUD, API anahtarı üretme/yenileme (ham anahtar yanıtta yalnız bir kez, <c>Cache-Control: no-store</c>),
/// aktif/pasif. İzinler komutlarda: attendance.terminal.view / attendance.terminal.manage. Silme yok (okutmalar terminale bağlı) - pasife alınır.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminals")]
[Authorize]
public sealed class TerminalsController : ApiControllerBase
{
    private const string NoStore = "no-store";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TerminalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTerminalsQuery(), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<TerminalDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetTerminalQuery(id), ct));

    /// <summary>Oluşturur ve ilk API anahtarını döner; anahtar bir daha gösterilmez.</summary>
    [HttpPost]
    [ProducesResponseType<TerminalApiKeyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] TerminalInput request, CancellationToken ct)
    {
        Response.Headers[HeaderNames.CacheControl] = NoStore;
        return FromResult(await Dispatcher.Send(new CreateTerminalCommand(request), ct));
    }

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] TerminalInput request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateTerminalCommand(id, request), ct));

    /// <summary>Yeni anahtar üretir; eski anahtar hemen geçersizleşir. Yeni anahtar yanıtta yalnız bir kez döner.</summary>
    [HttpPost(ApiRoutes.IdParam + "/api-key")]
    [ProducesResponseType<TerminalApiKeyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateApiKey(Guid id, CancellationToken ct)
    {
        Response.Headers[HeaderNames.CacheControl] = NoStore;
        return FromResult(await Dispatcher.Send(new RotateTerminalApiKeyCommand(id), ct));
    }

    [HttpPut(ApiRoutes.IdParam + "/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetTerminalActiveRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetTerminalActiveCommand(id, request.IsActive), ct));
}

/// <summary>Terminal grupları (Z5.1). Terminali olan grup silinemez (409).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/terminal-groups")]
[Authorize]
public sealed class TerminalGroupsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TerminalGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTerminalGroupsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] TerminalGroupRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateTerminalGroupCommand(request.Name), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] TerminalGroupRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RenameTerminalGroupCommand(id, request.Name), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteTerminalGroupCommand(id), ct));
}
