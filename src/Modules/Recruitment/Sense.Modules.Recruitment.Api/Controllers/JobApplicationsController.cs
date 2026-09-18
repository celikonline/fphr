using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.Applications;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to apply a candidate to a job posting.</summary>
public sealed record CreateJobApplicationRequest(Guid JobPostingId, Guid CandidateId);

/// <summary>Request to move an application to a new pipeline stage.</summary>
public sealed record MoveJobApplicationStageRequest(string Stage);

/// <summary>Application pipeline management (Kolay İK ATS "Başvurular"). R0: fixed stage set, manual moves - the automation rule engine is a later Recruitment package.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/applications")]
[Authorize]
public sealed class JobApplicationsController : ApiControllerBase
{
    /// <summary>Lists a job posting's applications.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<JobApplicationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByPosting([FromQuery] Guid jobPostingId, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListApplicationsByPostingQuery(jobPostingId, take), ct));

    /// <summary>Applies a candidate to a job posting.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateJobApplicationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateJobApplicationCommand(request.JobPostingId, request.CandidateId), ct));

    /// <summary>Moves an application to a new pipeline stage.</summary>
    [HttpPost(ApiRoutes.IdParam + "/stage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MoveStage(Guid id, [FromBody] MoveJobApplicationStageRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MoveJobApplicationStageCommand(id, request.Stage), ct));
}
