using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

public sealed record CreatePayItemAssignmentsRequest(
    Guid PayItemDefinitionId, IReadOnlyList<Guid> EmployeeIds, decimal? Value, int FromYear, int FromMonth, int? ToYear, int? ToMonth);

public sealed record UpdatePayItemAssignmentRequest(decimal? Value, int? ToYear, int? ToMonth);

/// <summary>Pay items assigned to employees (PAY-FORMULA). List needs payroll.trace.view; writes need payroll.payitem.manage
/// (enforced on the requests via [RequiresPermission]).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/pay-item-assignments")]
[Authorize]
public sealed class PayItemAssignmentsController : ApiControllerBase
{
    /// <summary>Assignments of one employee (employeeId) or covering a payroll period's month (periodId); one of the two is required.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PayItemAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? employeeId, [FromQuery] Guid? periodId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListPayItemAssignmentsQuery(employeeId, periodId), ct));

    [HttpPost]
    [ProducesResponseType<CreatePayItemAssignmentsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePayItemAssignmentsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreatePayItemAssignmentsCommand(
                request.PayItemDefinitionId, request.EmployeeIds, request.Value, request.FromYear, request.FromMonth, request.ToYear, request.ToMonth),
            ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePayItemAssignmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdatePayItemAssignmentCommand(id, request.Value, request.ToYear, request.ToMonth), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeletePayItemAssignmentCommand(id), ct));
}
