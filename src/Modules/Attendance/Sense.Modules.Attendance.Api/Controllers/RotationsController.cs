using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Rotations;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record SaveRotationRequest(string Code, string Name, RotationPreset Preset, int CycleLengthDays, IReadOnlyList<RotationStepDto> Steps);

public sealed record SetRotationActiveRequest(bool IsActive);

public sealed record ApplyRotationRequest(IReadOnlyList<Guid> EmployeeIds, DateOnly StartDate, DateOnly? EndDate, int OffsetDays);

/// <summary>Vardiya rotasyon kalıpları ve çalışana atama (Z3.3).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/rotations")]
[Authorize]
public sealed class RotationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RotationPatternDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListRotationsQuery(includeInactive), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SaveRotationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateRotationCommand(request.Code, request.Name, request.Preset, request.CycleLengthDays, request.Steps), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRotationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateRotationCommand(id, request.Code, request.Name, request.Preset, request.CycleLengthDays, request.Steps), ct));

    [HttpPatch("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetRotationActiveRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetRotationActiveCommand(id, request.IsActive), ct));

    /// <summary>Kalıbı çalışanlara uygular (taslak vardiya üretir); çakışmada 409, hiç atama yapılmaz.</summary>
    [HttpPost("{id:guid}/assignments")]
    [ProducesResponseType<ApplyRotationResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply(Guid id, [FromBody] ApplyRotationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ApplyRotationCommand(id, request.EmployeeIds, request.StartDate, request.EndDate, request.OffsetDays), ct));
}
