using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

/// <summary>Request body to submit a new visa document processing request.</summary>
public sealed record SubmitVisaDocumentRequestBody(
    Guid EmployeeId,
    Guid? VisaDocumentProcessId,
    DateTime TravelStart,
    DateTime TravelEnd,
    string CountryCode,
    string VisaType,
    string Description);

/// <summary>Visa document processing requests (senseik-001-vize-belgesi): submit, view own, and list by employee.
/// Approval/rejection are not exposed here directly - they're driven by the Workflow approval-completed event
/// (see the handoff doc's Application Layer Tasks), not a direct HTTP action on this resource.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/visa-requests")]
[Authorize]
public sealed class VisaDocumentRequestsController : ApiControllerBase
{
    /// <summary>Submits a new visa document processing request and starts its approval workflow.</summary>
    [HttpPost]
    [ProducesResponseType<SubmitVisaDocumentRequestResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitVisaDocumentRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new SubmitVisaDocumentRequestCommand(
                request.EmployeeId, request.VisaDocumentProcessId, request.TravelStart, request.TravelEnd,
                request.CountryCode, request.VisaType, request.Description),
            ct));

    /// <summary>Fetches a single visa document request's current status.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<VisaDocumentRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetVisaDocumentRequestQuery(id), ct));

    /// <summary>Lists an employee's visa document requests, optionally filtered by status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VisaDocumentRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid employeeId, [FromQuery] VisaDocumentRequestStatus? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListVisaDocumentRequestsQuery(employeeId, status), ct));
}

/// <summary>Visa document processes (predefined visa types the employee picks from when submitting a request).
/// Process CRUD (create/deactivate/reactivate) is a Settings-page/admin concern deferred with the rest of the
/// handoff doc's Web Layer Tasks - only the employee-facing active list is exposed today.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/visa-processes")]
[Authorize]
public sealed class VisaDocumentProcessesController : ApiControllerBase
{
    /// <summary>Lists the tenant's active visa document processes for the request-creation selection list.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VisaDocumentProcessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListVisaDocumentProcessesQuery(), ct));
}

/// <summary>Request body to configure the tenant's visa settings.</summary>
public sealed record UpdateVisaSettingsBody(Guid? ApproverEmployeeId, IReadOnlyList<Guid> TemplateDocumentIds);

/// <summary>Tenant-wide visa settings (approver + delivered template documents) - the Settings page's backing API
/// per the handoff doc's Web Layer Tasks.</summary>
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
