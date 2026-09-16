using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.Applications;
using Sense.Modules.Recruitment.Application.Candidates;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to create or update a candidate's contact information.</summary>
public sealed record CandidateContactRequest(string FirstName, string LastName, string Email, string? Phone);

/// <summary>Request to replace a candidate's tag set.</summary>
public sealed record SetCandidateTagsRequest(IReadOnlyList<Guid> TagIds);

/// <summary>Candidate management (Kolay İK ATS "Adaylar"). R0: added manually - the career-site apply flow is a later Recruitment package.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/candidates")]
[Authorize]
public sealed class CandidatesController : ApiControllerBase
{
    /// <summary>Lists candidates, optionally filtered by a name/email search term.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CandidateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCandidatesQuery(search), ct));

    /// <summary>A single candidate by id.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<CandidateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCandidateQuery(id), ct));

    /// <summary>Adds a candidate manually.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CandidateContactRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateCandidateCommand(request.FirstName, request.LastName, request.Email, request.Phone), ct));

    /// <summary>Edits a candidate's contact information.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CandidateContactRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateCandidateCommand(id, request.FirstName, request.LastName, request.Email, request.Phone), ct));

    /// <summary>Lists a candidate's applications (their history across postings).</summary>
    [HttpGet(ApiRoutes.IdParam + "/applications")]
    [ProducesResponseType<IReadOnlyList<JobApplicationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListApplications(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListApplicationsByCandidateQuery(id), ct));

    /// <summary>Replaces a candidate's tag set.</summary>
    [HttpPut(ApiRoutes.IdParam + "/tags")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetTags(Guid id, [FromBody] SetCandidateTagsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetCandidateTagsCommand(id, request.TagIds), ct));

    /// <summary>KVKK "unutulma hakkı": irreversibly scrubs one candidate's personal data on request.</summary>
    [HttpPost(ApiRoutes.IdParam + "/anonymize")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Anonymize(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AnonymizeCandidateCommand(id), ct));

    /// <summary>
    /// KVKK retention sweep: anonymizes every candidate whose retention window has elapsed. Returns the number of
    /// candidates anonymized. Safe to call repeatedly (only touches rows still due) - intended for a scheduled
    /// trigger until a recurring background job can be wired for it (see AnonymizeExpiredCandidatesCommand doc).
    /// </summary>
    [HttpPost("anonymize-expired")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AnonymizeExpired(CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AnonymizeExpiredCandidatesCommand(), ct));
}
