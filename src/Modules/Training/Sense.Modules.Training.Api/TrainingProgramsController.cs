using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Training.Application.Programs;
using Sense.Modules.Training.Contracts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Training.Api;

/// <summary>Eğitim programları (roadmap #72: ön koşul eğitim gating, bkz. <see cref="TrainingRequirementsController"/>).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/training/programs")]
[Authorize]
public sealed class TrainingProgramsController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTrainingProgram([FromBody] CreateTrainingProgramCommand command, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(command, ct));

    [HttpGet]
    [ProducesResponseType<List<TrainingProgramDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTrainingPrograms([FromQuery] bool? activeOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTrainingProgramsQuery(activeOnly), ct));

    [HttpPost(ApiRoutes.IdParam + "/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ActivateTrainingProgramCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloseTrainingProgramCommand(id), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<TrainingProgramDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrainingProgram(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetTrainingProgramQuery(id), ct));
}
