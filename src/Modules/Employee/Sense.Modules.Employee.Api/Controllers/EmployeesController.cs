using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Employees;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Request to create a minimal employee stub record.</summary>
public sealed record CreateEmployeeRequest(string DisplayName, Guid? UserId, Guid? OrgUnitId);

/// <summary>Minimal employee stub (Faz 0 madde 4): enough to be referenced by OrgUnit.ManagerEmployeeId; full personnel record is Faz 1.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees")]
[Authorize]
public sealed class EmployeesController : ApiControllerBase
{
    /// <summary>Lists minimal employee records for the current tenant (paginated).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<EmployeeListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeesQuery(paging), ct));

    /// <summary>Creates a minimal employee stub record.</summary>
    [HttpPost]
    [ProducesResponseType<EmployeeListItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateEmployeeCommand(request.DisplayName, request.UserId, request.OrgUnitId), ct));
}
