using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Training.Application.Requirements;
using Sense.Modules.Training.Contracts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Training.Api;

public sealed record CreateTrainingRequirementRequest(Guid TrainingProgramId, Guid PrerequisiteTrainingProgramId, bool BlocksAssignmentUntilCompleted);

/// <summary>Ön koşul eğitim kuralları (roadmap #72: "Ön koşul eğitim").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/training/requirements")]
[Authorize]
public sealed class TrainingRequirementsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<TrainingRequirementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid trainingProgramId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTrainingRequirementsQuery(trainingProgramId), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateTrainingRequirementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateTrainingRequirementCommand(request.TrainingProgramId, request.PrerequisiteTrainingProgramId, request.BlocksAssignmentUntilCompleted),
            ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteTrainingRequirementCommand(id), ct));
}
