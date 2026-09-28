using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

public sealed record SharedFileTypeRequest(string Name, bool VisibleOnMobile, int SortOrder = 0);

/// <summary>Shared file type catalog (E8): name and "mobilde görünsün" default.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/documents/shared-file-types")]
[Authorize]
public sealed class SharedFileTypesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SharedFileTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool? isActive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListSharedFileTypesQuery(isActive), ct));

    [HttpPost]
    [ProducesResponseType<SharedFileTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SharedFileTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateSharedFileTypeCommand(request.Name, request.VisibleOnMobile, request.SortOrder), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<SharedFileTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SharedFileTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateSharedFileTypeCommand(id, request.Name, request.VisibleOnMobile, request.SortOrder), ct));

    [HttpPost(ApiRoutes.IdParam + "/activate")]
    [ProducesResponseType<SharedFileTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetSharedFileTypeActiveCommand(id, true), ct));

    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType<SharedFileTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetSharedFileTypeActiveCommand(id, false), ct));
}
