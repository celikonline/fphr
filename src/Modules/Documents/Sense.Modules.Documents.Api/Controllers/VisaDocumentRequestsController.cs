using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

/// <summary>Request body to submit a new visa document request. The requester is always the signed-in employee.</summary>
public sealed record SubmitVisaDocumentRequestBody(
    Guid? VisaDocumentProcessId,
    DateTime TravelStart,
    DateTime TravelEnd,
    string CountryCode,
    string VisaType,
    string? Description);

/// <summary>The caller's own visa document requests (senseik-001-vize-belgesi): submit, list, view, and the documents
/// delivered after approval. Approval/rejection are not exposed here - the approver decides through the Workflow
/// inbox and the result reaches this module via the ApprovalCompleted/ApprovalRejected integration events.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/visa-requests")]
[Authorize]
public sealed class VisaDocumentRequestsController : ApiControllerBase
{
    /// <summary>Submits a new visa document request and starts its approval workflow.</summary>
    [HttpPost]
    [ProducesResponseType<SubmitVisaDocumentRequestResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitVisaDocumentRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new SubmitVisaDocumentRequestCommand(
                request.VisaDocumentProcessId, request.TravelStart, request.TravelEnd,
                request.CountryCode, request.VisaType, request.Description),
            ct));

    /// <summary>Lists the caller's own visa document requests, optionally filtered by status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VisaDocumentRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] VisaDocumentRequestStatus? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyVisaDocumentRequestsQuery(status), ct));

    /// <summary>Fetches one of the caller's own requests.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<VisaDocumentRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetVisaDocumentRequestQuery(id), ct));

    /// <summary>Documents delivered into the caller's folder for an approved request, with download URLs.</summary>
    [HttpGet(ApiRoutes.IdParam + "/documents")]
    [ProducesResponseType<IReadOnlyList<VisaDeliveredDocumentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Documents(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListVisaRequestDocumentsQuery(id), ct));
}

/// <summary>Request body to create or rename a visa document process.</summary>
public sealed record VisaDocumentProcessBody(string Name, string? CountryCode);

/// <summary>Request body to activate or deactivate a visa document process.</summary>
public sealed record SetVisaDocumentProcessActiveBody(bool IsActive);

/// <summary>Visa document processes (the templates an employee picks from when submitting a request). The employee
/// selection list is <c>GET visa-processes</c> (active only); the admin definition list and CRUD live under
/// <c>visa-processes/manage</c>.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/visa-processes")]
[Authorize]
public sealed class VisaDocumentProcessesController : ApiControllerBase
{
    /// <summary>Lists the tenant's active processes for the request form's selection list.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VisaDocumentProcessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListVisaDocumentProcessesQuery(), ct));

    /// <summary>Admin list: every process including inactive ones.</summary>
    [HttpGet("manage")]
    [ProducesResponseType<IReadOnlyList<VisaDocumentProcessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAll(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAllVisaDocumentProcessesQuery(), ct));

    /// <summary>Creates a process.</summary>
    [HttpPost("manage")]
    [ProducesResponseType<VisaDocumentProcessDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] VisaDocumentProcessBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateVisaDocumentProcessCommand(body.Name, body.CountryCode), ct));

    /// <summary>Renames a process / changes its country.</summary>
    [HttpPut("manage/" + ApiRoutes.IdParam)]
    [ProducesResponseType<VisaDocumentProcessDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] VisaDocumentProcessBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateVisaDocumentProcessCommand(id, body.Name, body.CountryCode), ct));

    /// <summary>Activates or deactivates a process.</summary>
    [HttpPut("manage/" + ApiRoutes.IdParam + "/active")]
    [ProducesResponseType<VisaDocumentProcessDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetVisaDocumentProcessActiveBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetVisaDocumentProcessActiveCommand(id, body.IsActive), ct));
}

/// <summary>Request body to configure the tenant's visa settings.</summary>
public sealed record UpdateVisaSettingsBody(Guid? ApproverEmployeeId, IReadOnlyList<Guid> TemplateDocumentIds);

/// <summary>Tenant-wide visa settings (approver + delivered template documents) - the settings page's backing API.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/visa-settings")]
[Authorize]
public sealed class VisaSettingsController : ApiControllerBase
{
    /// <summary>Reads the tenant's visa settings, seeding a default (unconfigured) row on first access.</summary>
    [HttpGet]
    [ProducesResponseType<VisaSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetVisaSettingsQuery(), ct));

    /// <summary>Configures the tenant's visa approver and delivered template documents.</summary>
    [HttpPut]
    [ProducesResponseType<VisaSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] UpdateVisaSettingsBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateVisaSettingsCommand(request.ApproverEmployeeId, request.TemplateDocumentIds), ct));
}
