using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.CostCenters;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to create a cost center.</summary>
public sealed record CreateCostCenterRequest(string Code, string Name);

/// <summary>Request to rename a cost center (code is immutable, see <see cref="CreateCostCenterRequest"/>).</summary>
public sealed record RenameCostCenterRequest(string Name);

/// <summary>Request to activate/deactivate a cost center.</summary>
public sealed record SetCostCenterActiveRequest(bool IsActive);
public sealed record SetEmployeeCostCenterOverrideRequest(Guid? CostCenterId);

/// <summary>
/// Cost center catalog management (S-4, roadmap Faz 2 item #2: "önce dar tasarla" - a tenant-scoped
/// code+name+active catalog; headcount/roster planning on top of it is a later slice). <b>Not yet wired
/// to a database</b>: the EF config/migration (see status/faz-02/MIGRATION-ISTEGI.md, ADR-0005) and the
/// <c>ICostCenterRepository</c> DI registration land together in a later turn - until then this
/// controller compiles but its handlers cannot resolve at runtime. Written now so the endpoint shape is
/// frozen for the FE (S-4 sözleşmesi, TALIMAT tur 3 md.4) ahead of the migration landing.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/cost-centers")]
[Authorize]
public sealed class CostCentersController : ApiControllerBase
{
    /// <summary>Flat list of cost centers for the current tenant, sorted by code.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CostCenterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCostCentersQuery(includeInactive), ct));

    /// <summary>Creates a cost center. 409 <c>organization.cost_center.code_taken</c> if the code is already used in this tenant.</summary>
    [HttpPost]
    [ProducesResponseType<CreateCostCenterResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateCostCenterRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateCostCenterCommand(request.Code, request.Name), ct));

    /// <summary>Renames a cost center; the code cannot be changed.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameCostCenterRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RenameCostCenterCommand(id, request.Name), ct));

    /// <summary>Activates or deactivates a cost center.</summary>
    [HttpPut(ApiRoutes.IdParam + "/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetCostCenterActiveRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetCostCenterActiveCommand(id, request.IsActive), ct));

    /// <summary>Returns an employee's effective cost center and its inheritance/override source.</summary>
    [HttpGet("employees/{employeeId:guid}/assignment")]
    [ProducesResponseType<EmployeeCostCenterAssignmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeAssignment(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeCostCenterAssignmentQuery(employeeId), ct));

    /// <summary>Sets an employee-specific override; send null to restore inheritance from the org-unit hierarchy.</summary>
    [HttpPut("employees/{employeeId:guid}/assignment")]
    [ProducesResponseType<EmployeeCostCenterAssignmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEmployeeAssignment(Guid employeeId, [FromBody] SetEmployeeCostCenterOverrideRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetEmployeeCostCenterOverrideCommand(employeeId, request.CostCenterId), ct));
}
