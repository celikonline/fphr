using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.AutomationRules;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to create a pipeline automation rule.</summary>
public sealed record CreateAutomationRuleRequest(string FromStage, string ToStage, Guid RequiredFormTemplateId);

/// <summary>Pipeline automation rules (roadmap #88 - mandatory-form-on-transition).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/automation-rules")]
[Authorize]
public sealed class AutomationRulesController : ApiControllerBase
{
    /// <summary>Lists pipeline automation rules, optionally active only.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AutomationRuleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListAutomationRulesQuery(activeOnly, take), ct));

    /// <summary>Creates a pipeline automation rule.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAutomationRuleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateAutomationRuleCommand(request.FromStage, request.ToStage, request.RequiredFormTemplateId), ct));

    /// <summary>Deactivates a pipeline automation rule.</summary>
    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateAutomationRuleCommand(id), ct));

    /// <summary>Reactivates a pipeline automation rule.</summary>
    [HttpPost(ApiRoutes.IdParam + "/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReactivateAutomationRuleCommand(id), ct));
}
