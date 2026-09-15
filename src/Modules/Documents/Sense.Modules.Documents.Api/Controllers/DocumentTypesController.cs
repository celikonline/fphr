using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

public sealed record CreateDocumentTypeRequest(
    string Code,
    string Name,
    DocumentTypeStage Stage,
    bool IsRequired,
    DocumentRequirementCondition? Condition,
    int? RenewalPeriodDays,
    int Quantity,
    bool IsSensitive,
    int? DefaultRetentionDays,
    int SortOrder);

public sealed record UpdateDocumentTypeRequest(
    string Name,
    bool IsRequired,
    int? RenewalPeriodDays,
    int Quantity,
    bool IsSensitive,
    int? DefaultRetentionDays,
    int SortOrder);

/// <summary>Tenant personnel document type catalog: view/create/edit/(de)activate.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/documents/types")]
[Authorize]
public sealed class DocumentTypesController : ApiControllerBase
{
    /// <summary>Lists the tenant's document type catalog (seeded on first read).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] DocumentTypeStage? stage, [FromQuery] bool? isActive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListDocumentTypesQuery(stage, isActive), ct));

    /// <summary>Creates a tenant-custom document type.</summary>
    [HttpPost]
    [ProducesResponseType<DocumentTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateDocumentTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateDocumentTypeCommand(
                request.Code, request.Name, request.Stage, request.IsRequired, request.Condition,
                request.RenewalPeriodDays, request.Quantity, request.IsSensitive, request.DefaultRetentionDays, request.SortOrder),
            ct));

    /// <summary>Edits a document type's editable fields (not Code/Stage/Condition/IsSystem).</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<DocumentTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateDocumentTypeCommand(
                id, request.Name, request.IsRequired, request.RenewalPeriodDays, request.Quantity, request.IsSensitive,
                request.DefaultRetentionDays, request.SortOrder),
            ct));

    /// <summary>Reactivates a document type.</summary>
    [HttpPost(ApiRoutes.IdParam + "/activate")]
    [ProducesResponseType<DocumentTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetDocumentTypeActiveCommand(id, true), ct));

    /// <summary>Deactivates a document type (stops resolving it for new hires; existing requirements are untouched).</summary>
    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType<DocumentTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetDocumentTypeActiveCommand(id, false), ct));
}
