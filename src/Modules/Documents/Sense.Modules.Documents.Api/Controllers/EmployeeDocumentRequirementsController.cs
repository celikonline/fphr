using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

public sealed record WaiveDocumentRequirementRequest(string Reason);

public sealed record LinkDocumentToRequirementRequest(Guid DocumentId);

/// <summary>Per-employee personnel document requirement tracker: HR view/report, waive/clear/link, self-service "my requirements".</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/documents")]
[Authorize]
public sealed class EmployeeDocumentRequirementsController : ApiControllerBase
{
    /// <summary>One employee's document requirements.</summary>
    [HttpGet("employees/{employeeId:guid}/requirements")]
    [ProducesResponseType<IReadOnlyList<EmployeeDocumentRequirementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForEmployee(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeDocumentRequirementsQuery(employeeId), ct));

    /// <summary>Self-service: the caller's own document requirements.</summary>
    [HttpGet("me/requirements")]
    [ProducesResponseType<IReadOnlyList<EmployeeDocumentRequirementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyDocumentRequirementsQuery(), ct));

    /// <summary>Paged report of missing/uploaded/waived requirements across the tenant.</summary>
    [HttpGet("requirements/report")]
    [ProducesResponseType<PagedResult<MissingDocumentReportItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report([FromQuery] DocumentRequirementStatus? status, [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMissingDocumentReportQuery(status, paging), ct));

    /// <summary>Waives a requirement with a mandatory reason.</summary>
    [HttpPost("requirements/{id:guid}/waive")]
    [ProducesResponseType<EmployeeDocumentRequirementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Waive(Guid id, [FromBody] WaiveDocumentRequirementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WaiveDocumentRequirementCommand(id, request.Reason), ct));

    /// <summary>Admin correction: undoes a waive, back to Missing.</summary>
    [HttpPost("requirements/{id:guid}/clear-waive")]
    [ProducesResponseType<EmployeeDocumentRequirementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearWaive(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ClearWaiveCommand(id), ct));

    /// <summary>Links an uploaded Document to a requirement, marking it satisfied.</summary>
    [HttpPost("requirements/{id:guid}/link")]
    [ProducesResponseType<EmployeeDocumentRequirementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Link(Guid id, [FromBody] LinkDocumentToRequirementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LinkDocumentToRequirementCommand(id, request.DocumentId), ct));

    /// <summary>Self-service: links an uploaded Document to the caller's own requirement, marking it satisfied.</summary>
    [HttpPost("me/requirements/{id:guid}/link")]
    [ProducesResponseType<EmployeeDocumentRequirementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkMine(Guid id, [FromBody] LinkDocumentToRequirementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LinkMyDocumentToRequirementCommand(id, request.DocumentId), ct));

    /// <summary>Re-runs requirement materialization for an existing employee.</summary>
    [HttpPost("employees/{employeeId:guid}/requirements/reevaluate")]
    [ProducesResponseType<IReadOnlyList<EmployeeDocumentRequirementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reevaluate(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReevaluateEmployeeDocumentRequirementsCommand(employeeId), ct));
}
