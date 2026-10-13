using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

public sealed record CreateIncentiveAssignmentsRequest(
    Guid IncentiveDefinitionId, IReadOnlyList<Guid> EmployeeIds, int FromYear, int FromMonth, int? ToYear, int? ToMonth, string? Note);

public sealed record UpdateIncentiveAssignmentRequest(int? ToYear, int? ToMonth, string? Note);

/// <summary>Incentives assigned to employees (PAY-INCENTIVE). List needs payroll.trace.view; writes need payroll.incentive.manage
/// (enforced on the requests via [RequiresPermission]).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/incentive-assignments")]
[Authorize]
public sealed class IncentiveAssignmentsController : ApiControllerBase
{
    /// <summary>Assignments of one employee (employeeId) or covering a payroll period's month (periodId); one of the two is required.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<IncentiveAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? employeeId, [FromQuery] Guid? periodId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListIncentiveAssignmentsQuery(employeeId, periodId), ct));

    /// <summary>Assigns one incentive to 1..500 employees; all-or-nothing.</summary>
    [HttpPost]
    [ProducesResponseType<CreateIncentiveAssignmentsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateIncentiveAssignmentsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateIncentiveAssignmentsCommand(
                request.IncentiveDefinitionId, request.EmployeeIds, request.FromYear, request.FromMonth, request.ToYear, request.ToMonth, request.Note),
            ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIncentiveAssignmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateIncentiveAssignmentCommand(id, request.ToYear, request.ToMonth, request.Note), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteIncentiveAssignmentCommand(id), ct));
}
