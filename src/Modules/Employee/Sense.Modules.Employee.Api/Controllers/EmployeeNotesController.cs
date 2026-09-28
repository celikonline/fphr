using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

public sealed record EmployeeNoteRequest(string Title, string? Content, EmployeeNoteImportance Importance);

public sealed record CreateEmployeeNoteRequest(Guid EmployeeId, string Title, string? Content, EmployeeNoteImportance Importance);

public sealed record InitiateNoteAttachmentRequest(string FileName, string ContentType, long FileSizeBytes);

public sealed record ConfirmNoteAttachmentRequest(string ETag);

/// <summary>HR/manager notes on employees (E7). Needs employee.notes.view / employee.notes.manage; the self-service (my) permissions never grant access.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employee-notes")]
[Authorize]
public sealed class EmployeeNotesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId, [FromQuery] EmployeeNoteImportance? importance, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListEmployeeNotesQuery(employeeId, importance, page, pageSize), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeNoteRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateEmployeeNoteCommand(request.EmployeeId, request.Title, request.Content, request.Importance), ct));

    [HttpPut(ApiRoutes.IdParam)]
    public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeNoteRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateEmployeeNoteCommand(id, request.Title, request.Content, request.Importance), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteEmployeeNoteCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/attachment/initiate")]
    public async Task<IActionResult> InitiateAttachment(Guid id, [FromBody] InitiateNoteAttachmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new InitiateNoteAttachmentUploadCommand(id, request.FileName, request.ContentType, request.FileSizeBytes), ct));

    [HttpPost(ApiRoutes.IdParam + "/attachment/confirm")]
    public async Task<IActionResult> ConfirmAttachment(Guid id, [FromBody] ConfirmNoteAttachmentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ConfirmNoteAttachmentUploadCommand(id, request.ETag), ct));

    [HttpDelete(ApiRoutes.IdParam + "/attachment")]
    public async Task<IActionResult> DeleteAttachment(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteNoteAttachmentCommand(id), ct));
}
