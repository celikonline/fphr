using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Puantaj yeniden hesaplama (zaman-devam Z0.3): arka plan işi kuyruğa alır, ilerleme sorgulanır. İzin: attendance.recalculate.run.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/recalculation")]
[Authorize]
public sealed class RecalculationController : ApiControllerBase
{
    public sealed record RecalculationRequest(DateOnly From, DateOnly To, IReadOnlyCollection<Guid>? EmployeeIds);

    public sealed record RecalculationJobDto(Guid JobId, int SkippedOutOfScope);

    [HttpPost]
    [ProducesResponseType<RecalculationJobDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Enqueue([FromBody] RecalculationRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new EnqueueRecalculationCommand(request.From, request.To, request.EmployeeIds), ct);
        return result.IsSuccess ? Accepted(new RecalculationJobDto(result.Value.JobId, result.Value.SkippedOutOfScope)) : Problem(result.Error);
    }

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType<RecalculationStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(Guid jobId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetRecalculationStatusQuery(jobId), ct));
}
