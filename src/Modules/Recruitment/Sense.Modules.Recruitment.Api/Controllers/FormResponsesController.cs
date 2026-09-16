using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.FormResponses;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to submit a form response.</summary>
public sealed record SubmitFormResponseRequest(Guid TemplateId, Guid JobApplicationId, IReadOnlyList<FieldAnswerInput> Answers);

/// <summary>Form responses (roadmap flow #3 "Mülakat geri bildirimi"; aday kartı "Formlar" sekmesi).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/form-responses")]
[Authorize]
public sealed class FormResponsesController : ApiControllerBase
{
    /// <summary>Lists an application's submitted form responses.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FormResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByApplication([FromQuery] Guid jobApplicationId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListFormResponsesByApplicationQuery(jobApplicationId), ct));

    /// <summary>Submits a form response for an application.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitFormResponseRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitFormResponseCommand(request.TemplateId, request.JobApplicationId, request.Answers), ct));
}
