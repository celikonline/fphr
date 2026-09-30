using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Workflow.Application.Delegations;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Workflow.Api.Controllers;

/// <summary>Request to create an approval delegation for the caller.</summary>
public sealed record CreateMyDelegationRequest(Guid DelegateEmployeeId, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<string>? RequestTypes);

/// <summary>Admin request to create an approval delegation for any employee.</summary>
public sealed record CreateDelegationRequest(Guid DelegatorEmployeeId, Guid DelegateEmployeeId, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<string>? RequestTypes);

/// <summary>Approval delegations (vekalet, Z2.2): an employee hands their approval authority to another for a date range.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/workflow/delegations")]
[Authorize]
public sealed class DelegationsController : ApiControllerBase
{
    /// <summary>Delegations the caller gave or received.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<IReadOnlyList<DelegationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyDelegationsQuery(), ct));

    /// <summary>The caller delegates their own approvals.</summary>
    [HttpPost("mine")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateMine([FromBody] CreateMyDelegationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateMyDelegationCommand(request.DelegateEmployeeId, request.StartDate, request.EndDate, request.RequestTypes), ct));

    /// <summary>The caller revokes their own delegation.</summary>
    [HttpDelete("mine/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeMine(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeMyDelegationCommand(id), ct));

    /// <summary>Admin: every delegation in the tenant.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DelegationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListDelegationsQuery(), ct));

    /// <summary>Admin: creates a delegation on behalf of any employee.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateDelegationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateDelegationCommand(request.DelegatorEmployeeId, request.DelegateEmployeeId, request.StartDate, request.EndDate, request.RequestTypes), ct));

    /// <summary>Admin: revokes any delegation.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeDelegationCommand(id), ct));
}
