using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.OccupationalSafety.Application.RiskAssessments;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.OccupationalSafety.Api.Controllers;

public sealed record RecordRiskAssessmentRequest(Guid LocationId, DateOnly AssessmentDate, string PreparedBy, string? Note);

/// <summary>Recorded risk assessments ("Risk Değerlendirmesi Yönetmeliği"); renewal due date derived server-side from the location's hazard class.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/ohs/risk-assessments")]
[Authorize]
public sealed class RiskAssessmentsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RiskAssessmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListRiskAssessmentsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Record([FromBody] RecordRiskAssessmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new RecordRiskAssessmentCommand(request.LocationId, request.AssessmentDate, request.PreparedBy, request.Note), ct));
}
