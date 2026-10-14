using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees")]
[Authorize]
public sealed class PersonnelNumbersController : ApiControllerBase
{
    [HttpGet("personnel-number-ranges")]
    public async Task<IActionResult> Ranges([FromQuery] bool includeInactive, CancellationToken ct) => FromResult(await Dispatcher.Query(new ListPersonnelNumberRangesQuery(includeInactive), ct));
    [HttpPost("personnel-number-ranges")]
    public async Task<IActionResult> CreateRange(CreatePersonnelNumberRangeCommand command, CancellationToken ct) => FromResult(await Dispatcher.Send(command, ct));
    [HttpPut("personnel-number-ranges/{id:guid}")]
    public async Task<IActionResult> UpdateRange(Guid id, UpdatePersonnelNumberRangeRequest request, CancellationToken ct) => FromResult(await Dispatcher.Send(new UpdatePersonnelNumberRangeCommand(id, request.Name, request.EndNumber), ct));
    [HttpPost("personnel-number-ranges/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateRange(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new SetPersonnelNumberRangeActiveCommand(id, false), ct));
    [HttpPost("personnel-number-ranges/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateRange(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new SetPersonnelNumberRangeActiveCommand(id, true), ct));
    [HttpGet("personnel-number-rules")]
    public async Task<IActionResult> Rules(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListPersonnelNumberRulesQuery(), ct));
    [HttpPost("personnel-number-rules")]
    public async Task<IActionResult> CreateRule(CreatePersonnelNumberRuleCommand command, CancellationToken ct) => FromResult(await Dispatcher.Send(command, ct));
    [HttpPut("personnel-number-rules/{id:guid}")]
    public async Task<IActionResult> UpdateRule(Guid id, UpdatePersonnelNumberRuleRequest request, CancellationToken ct) => FromResult(await Dispatcher.Send(new UpdatePersonnelNumberRuleCommand(id, request.RangeId, request.IsActive), ct));
    [HttpGet("personnel-number-assignment")]
    public async Task<IActionResult> Assignment([FromQuery] Guid? orgUnitId, [FromQuery] PersonnelNumberOperation operation = PersonnelNumberOperation.Hire, CancellationToken ct = default) => FromResult(await Dispatcher.Query(new PreviewPersonnelNumberQuery(orgUnitId, operation), ct));
    [HttpGet("personnel-number-companies")]
    public async Task<IActionResult> Companies(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListPersonnelNumberCompaniesQuery(), ct));
}
public sealed record UpdatePersonnelNumberRangeRequest(string Name, string EndNumber);
public sealed record UpdatePersonnelNumberRuleRequest(Guid RangeId, bool IsActive);
