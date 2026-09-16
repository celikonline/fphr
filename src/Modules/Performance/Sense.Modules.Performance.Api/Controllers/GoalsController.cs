using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

public sealed record AddGoalRequest(
    Guid ParticipantEmployeeId, Guid CategoryId, string Name, string? Description, decimal TargetValue,
    IReadOnlyList<GoalScaleRangeDto> Scale, decimal? Weight);

public sealed record ParticipantScopeRequest(Guid ParticipantEmployeeId);

public sealed record EnterGoalResultRequest(Guid ParticipantEmployeeId, decimal ActualValue, string? ResultComment);

/// <summary>Hedef akışı (yalnız Kind=CompetencyAndGoal): Hedef ekle → Hedefleri onayla → Sonuç gir → Sonuçları onayla.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/cycles/{cycleId:guid}/participants/{participantId:guid}/goals")]
[Authorize]
public sealed class GoalsController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>"Hedef ekle".</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Add(Guid cycleId, Guid participantId, [FromBody] AddGoalRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddGoalCommand(
                cycleId, participantId, request.ParticipantEmployeeId, request.CategoryId, request.Name, request.Description,
                request.TargetValue, request.Scale, request.Weight, CurrentUser.EmployeeId ?? Guid.Empty),
            ct));

    /// <summary>"Hedefleri onayla": katılımcının tüm Taslak hedeflerini toplu onaylar.</summary>
    [HttpPost("approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid cycleId, Guid participantId, [FromBody] ParticipantScopeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new ApproveGoalsCommand(cycleId, participantId, request.ParticipantEmployeeId, CurrentUser.EmployeeId ?? Guid.Empty), ct));

    /// <summary>"Sonuç gir".</summary>
    [HttpPost("{goalId:guid}/result")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EnterResult(Guid cycleId, Guid participantId, Guid goalId, [FromBody] EnterGoalResultRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new EnterGoalResultCommand(
                cycleId, goalId, request.ParticipantEmployeeId, request.ActualValue, request.ResultComment, CurrentUser.EmployeeId ?? Guid.Empty),
            ct));

    /// <summary>"Sonuçları onayla": katılımcının tüm "Sonuç girildi" hedeflerini toplu onaylar.</summary>
    [HttpPost("approve-results")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ApproveResults(Guid cycleId, Guid participantId, [FromBody] ParticipantScopeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new ApproveGoalResultsCommand(cycleId, participantId, request.ParticipantEmployeeId, CurrentUser.EmployeeId ?? Guid.Empty), ct));
}
