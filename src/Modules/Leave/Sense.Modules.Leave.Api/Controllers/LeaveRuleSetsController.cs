using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Absences;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

/// <summary>İzinler → Kurallar: rule sets and their leave types.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/rule-sets")]
[Authorize]
public sealed class LeaveRuleSetsController : ApiControllerBase
{
    private const string TypeRoute = ApiRoutes.IdParam + "/types/{typeId:guid}";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LeaveRuleSetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListLeaveRuleSetsQuery(take), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<LeaveRuleSetDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetLeaveRuleSetQuery(id), ct));

    /// <summary>"Kopyala ve düzenle".</summary>
    [HttpPost(ApiRoutes.IdParam + "/clone")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneLeaveRuleSetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloneLeaveRuleSetCommand(id, request.Name), ct));

    [HttpPut(ApiRoutes.IdParam + "/name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameLeaveRuleSetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RenameLeaveRuleSetCommand(id, request.Name), ct));

    [HttpPost(ApiRoutes.IdParam + "/set-default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetDefaultLeaveRuleSetCommand(id), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteLeaveRuleSetCommand(id), ct));

    /// <summary>Assigns the rule set to employees and recalculates their balances; warnings are non-blocking.</summary>
    [HttpPost(ApiRoutes.IdParam + "/assign")]
    [ProducesResponseType<LeaveRuleSetAssignmentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignLeaveRuleSetRequest request, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Send(new AssignLeaveRuleSetCommand(id, request.EmployeeIds ?? []), ct)).Localize(localizer));

    [HttpPost(ApiRoutes.IdParam + "/types")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddType(Guid id, [FromBody] LeaveTypeSettings request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddLeaveTypeCommand(id, request), ct));

    [HttpPut(TypeRoute)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateType(Guid id, Guid typeId, [FromBody] LeaveTypeSettings request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateLeaveTypeCommand(id, typeId, request), ct));

    [HttpPut(TypeRoute + "/disabled")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetTypeDisabled(Guid id, Guid typeId, [FromBody] SetLeaveTypeDisabledRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetLeaveTypeDisabledCommand(id, typeId, request.Disabled), ct));
}
