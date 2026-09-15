using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

public sealed record SubmitReviewResponseRequest(IReadOnlyList<ReviewAnswerInputDto> Answers);

/// <summary>Değerlendirici tarafı: kendisine atanan formu doldurma.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/cycles/{cycleId:guid}/assignments")]
[Authorize]
public sealed class AssignmentsController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>Bir atamanın tüm sorularını tek seferde cevaplar (yetkinlik formu → tamamla).</summary>
    [HttpPost("{assignmentId:guid}/submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid cycleId, Guid assignmentId, [FromBody] SubmitReviewResponseRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new SubmitReviewResponseCommand(cycleId, assignmentId, CurrentUser.EmployeeId ?? Guid.Empty, request.Answers), ct));
}
