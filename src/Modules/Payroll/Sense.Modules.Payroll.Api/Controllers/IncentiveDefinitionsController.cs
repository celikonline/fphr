using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Payroll.Application.Incentives;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Payroll.Api.Controllers;

/// <summary>Create body of an incentive definition. <paramref name="ValidFrom"/> defaults to today.</summary>
public sealed record CreateIncentiveDefinitionRequest(
    string Code,
    string Name,
    string? LawReference,
    string? DeclarationCode,
    IncentiveKind Kind,
    IncentiveParametersDto? Parameters,
    int? MaxMonths,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    internal IncentiveDefinitionInput ToInput() => new(Name, LawReference, DeclarationCode, Kind, Parameters, MaxMonths, ValidFrom, ValidTo);
}

/// <summary>Update body: everything but the code. A calculation-relevant change drops the SMMM approval.</summary>
public sealed record UpdateIncentiveDefinitionRequest(
    string Name,
    string? LawReference,
    string? DeclarationCode,
    IncentiveKind Kind,
    IncentiveParametersDto? Parameters,
    int? MaxMonths,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    internal IncentiveDefinitionInput ToInput() => new(Name, LawReference, DeclarationCode, Kind, Parameters, MaxMonths, ValidFrom, ValidTo);
}

public sealed record IncentiveFromTemplateRequest(string TemplateKey);

public sealed record IncentiveApprovalNoteRequest(string? Note);

/// <summary>Bordro > Teşvikler (PAY-INCENTIVE): tenant-wide incentive models. Reads need payroll.trace.view; writes need
/// payroll.incentive.manage and approval/revocation payroll.incentive.approve (enforced on the requests via [RequiresPermission]).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/payroll/incentive-definitions")]
[Authorize]
public sealed class IncentiveDefinitionsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<IncentiveDefinitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListIncentiveDefinitionsQuery(includeInactive), ct));

    /// <summary>The in-code template catalog with a flag per template saying whether this tenant already created it.</summary>
    [HttpGet("templates")]
    [ProducesResponseType<IReadOnlyList<IncentiveTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Templates(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListIncentiveTemplatesQuery(), ct));

    /// <summary>The legislation parameters a rate may reference, with their value in force today.</summary>
    [HttpGet("parameter-catalog")]
    [ProducesResponseType<IncentiveParameterCatalogDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ParameterCatalog(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetIncentiveParameterCatalogQuery(), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<IncentiveDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetIncentiveDefinitionQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateIncentiveDefinitionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateIncentiveDefinitionCommand(request.Code, request.ToInput()), ct));

    /// <summary>Creates a definition from the catalog; a second call for the same template is a 409.</summary>
    [HttpPost("from-template")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FromTemplate([FromBody] IncentiveFromTemplateRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateIncentiveFromTemplateCommand(request.TemplateKey), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIncentiveDefinitionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateIncentiveDefinitionCommand(id, request.ToInput()), ct));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] IncentiveApprovalNoteRequest? request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ApproveIncentiveDefinitionCommand(id, request?.Note), ct));

    [HttpPost("{id:guid}/revoke-approval")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeApproval(Guid id, [FromBody] IncentiveApprovalNoteRequest? request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeIncentiveApprovalCommand(id, request?.Note), ct));

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ActivateIncentiveDefinitionCommand(id), ct));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateIncentiveDefinitionCommand(id), ct));
}
