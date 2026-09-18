using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.JobRequisitions;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to create a job requisition.</summary>
public sealed record CreateJobRequisitionRequest(string Title, Guid OrgUnitId, string EmploymentType, int HeadcountRequested, string Reason, string? Justification);

/// <summary>Job requisition management (Kolay İK "Talep formu"; R1 roadmap #87 - approval via Workflow).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/requisitions")]
[Authorize]
public sealed class JobRequisitionsController : ApiControllerBase
{
    /// <summary>Lists job requisitions, optionally filtered to one status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<JobRequisitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListJobRequisitionsQuery(status, take), ct));

    /// <summary>A single job requisition by id.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<JobRequisitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetJobRequisitionQuery(id), ct));

    /// <summary>Creates a job requisition in Draft status.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateJobRequisitionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateJobRequisitionCommand(request.Title, request.OrgUnitId, request.EmploymentType, request.HeadcountRequested, request.Reason, request.Justification),
            ct));

    /// <summary>Submits a requisition for approval (Draft -> PendingApproval), starting a Workflow "Requisition" approval process.</summary>
    [HttpPost(ApiRoutes.IdParam + "/submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitJobRequisitionCommand(id), ct));

    /// <summary>Cancels a requisition (Draft/PendingApproval -> Cancelled).</summary>
    [HttpPost(ApiRoutes.IdParam + "/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CancelJobRequisitionCommand(id), ct));
}
