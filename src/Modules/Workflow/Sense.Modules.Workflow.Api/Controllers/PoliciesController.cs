using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Workflow.Application.ApprovalPolicies;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Request to create an approval policy.</summary>
public sealed record CreatePolicyRequest(string RequestType, string Name, bool ShowCurrentApproverToRequester, IReadOnlyList<ApprovalStepInput> Steps);

/// <summary>Request to update an approval policy.</summary>
public sealed record UpdatePolicyRequest(string Name, bool ShowCurrentApproverToRequester, IReadOnlyList<ApprovalStepInput> Steps);

/// <summary>Approval policy management (Kolay İK "Yeni süreç ekle").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/policies")]
[Authorize]
public sealed class PoliciesController : ApiControllerBase
{
    /// <summary>Lists approval policies, optionally filtered to one request type.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApprovalPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? requestType, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPoliciesQuery(requestType), ct));

    /// <summary>A single approval policy by id.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<ApprovalPolicyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetPolicyQuery(id), ct));

    /// <summary>Creates a named approval policy.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePolicyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreatePolicyCommand(request.RequestType, request.Name, request.ShowCurrentApproverToRequester, request.Steps),
            ct));

    /// <summary>Renames/replaces the step list of/changes the visibility of an approval policy.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePolicyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdatePolicyCommand(id, request.Name, request.ShowCurrentApproverToRequester, request.Steps),
            ct));

    /// <summary>Deactivates (soft-deletes) an approval policy; in-flight requests already snapshotted its steps and are unaffected.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivatePolicyCommand(id), ct));
}
