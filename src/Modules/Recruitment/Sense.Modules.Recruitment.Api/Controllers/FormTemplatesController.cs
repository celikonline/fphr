using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.FormTemplates;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to create a form template.</summary>
public sealed record CreateFormTemplateRequest(string Name, string TemplateType, IReadOnlyList<FormFieldInputDto> Fields);

/// <summary>Request to replace a form template's fields.</summary>
public sealed record ReplaceFormTemplateFieldsRequest(IReadOnlyList<FormFieldInputDto> Fields);

/// <summary>Form template management (roadmap #86 - Feedback/Scorecard/Outcome).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/form-templates")]
[Authorize]
public sealed class FormTemplatesController : ApiControllerBase
{
    /// <summary>Lists form templates, optionally active only.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FormTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListFormTemplatesQuery(activeOnly, take), ct));

    /// <summary>A single form template by id.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<FormTemplateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetFormTemplateQuery(id), ct));

    /// <summary>Creates a form template.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateFormTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateFormTemplateCommand(request.Name, request.TemplateType, request.Fields), ct));

    /// <summary>Replaces a template's field set.</summary>
    [HttpPut(ApiRoutes.IdParam + "/fields")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceFields(Guid id, [FromBody] ReplaceFormTemplateFieldsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReplaceFormTemplateFieldsCommand(id, request.Fields), ct));

    /// <summary>Deactivates a form template.</summary>
    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateFormTemplateCommand(id), ct));

    /// <summary>Reactivates a form template.</summary>
    [HttpPost(ApiRoutes.IdParam + "/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReactivateFormTemplateCommand(id), ct));
}
