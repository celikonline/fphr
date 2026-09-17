using Microsoft.AspNetCore.Mvc;
using Sense.Shared.Contracts.Commands;
using Sense.Shared.Contracts.Queries;
using Sense.Shared.Web.Authorization;
using Sense.Modules.Training.Application.Programs;

namespace Sense.Modules.Training.Api;

[ApiController]
[Route("api/v1/training/programs")]
[Authorize(Policy = "Bearer")]
public class TrainingProgramsController : ControllerBase
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ITenantContext _tenantContext;

    public TrainingProgramsController(
        ICommandDispatcher commandDispatcher,
        IQueryDispatcher queryDispatcher,
        ITenantContext tenantContext)
    {
        _commandDispatcher = commandDispatcher;
        _queryDispatcher = queryDispatcher;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize("training.programs.manage")]
    public async Task<ActionResult<Guid>> CreateTrainingProgram(
        [FromBody] CreateTrainingProgramCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _commandDispatcher.DispatchAsync(
            command with { TenantId = _tenantContext.TenantId },
            cancellationToken);

        return result.IsSuccess ? Created(string.Empty, result.Value) : BadRequest(result.Error);
    }

    [HttpGet]
    [Authorize("training.programs.view")]
    public async Task<ActionResult<List<TrainingProgramDto>>> ListTrainingPrograms(
        [FromQuery] bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        var query = new ListTrainingProgramsQuery(_tenantContext.TenantId, activeOnly);
        var result = await _queryDispatcher.DispatchAsync(query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{id}")]
    [Authorize("training.programs.view")]
    public async Task<ActionResult<TrainingProgramDto>> GetTrainingProgram(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTrainingProgramQuery(id, _tenantContext.TenantId);
        var result = await _queryDispatcher.DispatchAsync(query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
