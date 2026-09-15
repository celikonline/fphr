using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.CustomFields;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Create/update body of a custom field definition (dataType/section as camelCase enum strings; Options ignored unless a Select type).</summary>
public sealed record CustomFieldDefinitionRequest(
    string? Key,
    string Name,
    CustomFieldDataType DataType,
    CustomFieldSection Section,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    bool IsEmployeeEditable,
    bool EmployeeEditRequiresApproval,
    bool IsReportable = true,
    bool IsSensitive = false,
    int SortOrder = 0);

/// <summary>
/// Tenant admin catalog of employee custom field definitions ("Özel alan tanımı genişletme", 11.3.03 E4). Values on
/// individual employee cards are read/written through <see cref="EmployeesController"/> instead.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees/custom-field-definitions")]
[Authorize]
public sealed class CustomFieldDefinitionsController : ApiControllerBase
{
    /// <summary>Definitions ordered by SortOrder then Name; inactive ones included only when asked (admin view).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CustomFieldDefinitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCustomFieldDefinitionsQuery(includeInactive), ct));

    [HttpPost]
    [ProducesResponseType<CustomFieldDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CustomFieldDefinitionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateCustomFieldDefinitionCommand(
                request.Key ?? string.Empty, request.Name, request.DataType, request.Section, request.Options,
                request.IsRequired, request.IsEmployeeEditable, request.EmployeeEditRequiresApproval, request.IsReportable, request.IsSensitive, request.SortOrder),
            ct));

    /// <summary>Updates everything but Key/DataType (both immutable once created; the request's values for them are ignored).</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<CustomFieldDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] CustomFieldDefinitionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateCustomFieldDefinitionCommand(
                id, request.Name, request.Section, request.Options, request.IsRequired, request.IsEmployeeEditable,
                request.EmployeeEditRequiresApproval, request.IsReportable, request.IsSensitive, request.SortOrder),
            ct));

    /// <summary>Soft-deactivates a definition (never hard-deleted - employee values may still reference it).</summary>
    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType<CustomFieldDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateCustomFieldDefinitionCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/reactivate")]
    [ProducesResponseType<CustomFieldDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReactivateCustomFieldDefinitionCommand(id), ct));
}
