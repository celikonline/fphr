using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.Goals;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

/// <summary>Request to create a goal for a given employee (HR/manager).</summary>
public sealed record CreateGoalRequest(Guid EmployeeId, string Title, string? Description, DateOnly TargetDate, Guid? ReviewCycleId);

/// <summary>Request to create a goal for the caller themselves (self-service).</summary>
public sealed record CreateMyGoalRequest(string Title, string? Description, DateOnly TargetDate, Guid? ReviewCycleId);

/// <summary>Request to update a goal's progress percentage.</summary>
public sealed record UpdateGoalProgressRequest(int Progress);

/// <summary>Goal/OKR management: self-service ("Hedeflerim"), employee-scoped (İK/yönetici) and team-scoped (yönetici) views.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/goals")]
[Authorize]
public sealed class GoalsController : ApiControllerBase
{
    /// <summary>The caller's own goals.</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<GoalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyGoalsQuery(), ct));

    /// <summary>The manager's team goals (caller's "performance.team.goals" data scope).</summary>
    [HttpGet("team")]
    [ProducesResponseType<IReadOnlyList<GoalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTeam(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTeamGoalsQuery(), ct));

    /// <summary>A specific employee's goals (HR/manager).</summary>
    [HttpGet("employee/{employeeId:guid}")]
    [ProducesResponseType<IReadOnlyList<GoalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByEmployee(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListGoalsByEmployeeQuery(employeeId), ct));

    /// <summary>Creates a goal for an employee (HR/manager).</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateGoalRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateGoalCommand(request.EmployeeId, request.Title, request.Description, request.TargetDate, request.ReviewCycleId), ct));

    /// <summary>Creates a goal for the caller themselves.</summary>
    [HttpPost("me")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateMine([FromBody] CreateMyGoalRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateMyGoalCommand(request.Title, request.Description, request.TargetDate, request.ReviewCycleId), ct));

    /// <summary>Updates a goal's progress (HR/manager).</summary>
    [HttpPost(ApiRoutes.IdParam + "/progress")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateProgress(Guid id, [FromBody] UpdateGoalProgressRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateGoalProgressCommand(id, request.Progress), ct));

    /// <summary>Updates the caller's own goal's progress.</summary>
    [HttpPost(ApiRoutes.IdParam + "/progress/me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateMyProgress(Guid id, [FromBody] UpdateGoalProgressRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateMyGoalProgressCommand(id, request.Progress), ct));

    /// <summary>Cancels a goal (HR/manager).</summary>
    [HttpPost(ApiRoutes.IdParam + "/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CancelGoalCommand(id), ct));
}
