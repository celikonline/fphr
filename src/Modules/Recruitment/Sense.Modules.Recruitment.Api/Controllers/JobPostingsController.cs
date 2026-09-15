using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.JobPostings;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to create a job posting.</summary>
public sealed record CreateJobPostingRequest(
    string Title, Guid OrgUnitId, Guid? LocationId, string EmploymentType, string? Description, Guid OwnerUserId, Guid? HiringManagerUserId);

/// <summary>Request to update a job posting's fields.</summary>
public sealed record UpdateJobPostingRequest(
    string Title, Guid OrgUnitId, Guid? LocationId, string EmploymentType, string? Description, string? CompanyNotes,
    Guid? HiringManagerUserId, DateOnly? ClosingDate);

/// <summary>Request to archive a job posting.</summary>
public sealed record ArchiveJobPostingRequest(string Reason);

/// <summary>Request to replace a job posting's access list.</summary>
public sealed record SetJobPostingAccessRequest(IReadOnlyList<Guid> UserIds);

/// <summary>Job posting management (Kolay İK ATS "İlanlar").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/postings")]
[Authorize]
public sealed class JobPostingsController : ApiControllerBase
{
    /// <summary>Lists job postings, optionally filtered to one status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<JobPostingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListJobPostingsQuery(status), ct));

    /// <summary>A single job posting by id.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<JobPostingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetJobPostingQuery(id), ct));

    /// <summary>Creates a job posting in Draft status.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateJobPostingRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateJobPostingCommand(
                request.Title, request.OrgUnitId, request.LocationId, request.EmploymentType, request.Description,
                request.OwnerUserId, request.HiringManagerUserId),
            ct));

    /// <summary>Edits a job posting's fields (does not change its status).</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateJobPostingRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateJobPostingCommand(
                id, request.Title, request.OrgUnitId, request.LocationId, request.EmploymentType, request.Description,
                request.CompanyNotes, request.HiringManagerUserId, request.ClosingDate),
            ct));

    /// <summary>Publishes a posting (Draft/OnHold/Closed -> Open).</summary>
    [HttpPost(ApiRoutes.IdParam + "/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new PublishJobPostingCommand(id), ct));

    /// <summary>Closes an open posting (reversible via Publish).</summary>
    [HttpPost(ApiRoutes.IdParam + "/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloseJobPostingCommand(id), ct));

    /// <summary>Archives a posting (terminal).</summary>
    [HttpPost(ApiRoutes.IdParam + "/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveJobPostingRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ArchiveJobPostingCommand(id, request.Reason), ct));

    /// <summary>Replaces a posting's explicit access list.</summary>
    [HttpPut(ApiRoutes.IdParam + "/access")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetAccess(Guid id, [FromBody] SetJobPostingAccessRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetJobPostingAccessCommand(id, request.UserIds), ct));
}
