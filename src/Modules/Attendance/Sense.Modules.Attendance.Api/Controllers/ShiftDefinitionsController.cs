using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.ShiftDefinitions;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record SetShiftDefinitionActiveRequest(bool IsActive);

/// <summary>Vardiya kataloğu (zaman-devam Z0.2): tanımlar, toleranslar, mola listesi ve "Kaydet &amp; Geçmişe Uygula".</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/shift-definitions")]
[Authorize]
public sealed class ShiftDefinitionsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ShiftDefinitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListShiftDefinitionsQuery(includeInactive), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<ShiftDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetShiftDefinitionQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] ShiftDefinitionInput request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateShiftDefinitionCommand(request), ct));

    /// <summary>"Kaydet"; <paramref name="applyToPast"/> = "Kaydet &amp; Geçmişe Uygula" (<paramref name="applyFrom"/> yoksa tüm geçmiş atamalar).</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<ShiftDefinitionSaveResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] ShiftDefinitionInput request, [FromQuery] bool applyToPast, [FromQuery] DateOnly? applyFrom, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateShiftDefinitionCommand(id, request, applyToPast, applyFrom), ct));

    [HttpPut(ApiRoutes.IdParam + "/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetShiftDefinitionActiveRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetShiftDefinitionActiveCommand(id, request.IsActive), ct));

    /// <summary>Kullanımdaki (atama/rotasyon/şablon) tanım silinemez: 409, yerine pasife alınır.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteShiftDefinitionCommand(id), ct));
}
