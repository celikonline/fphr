using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Application.HrMessages;
using Sense.Modules.Notifications.Domain.HrMessages;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Notifications.Api.Controllers;

public sealed record SendHrMessageBody(
    string Subject,
    string Body,
    HrMessageImportance Importance,
    IReadOnlyList<string> Channels,
    EmployeeSelection? Selection,
    IReadOnlyList<Guid>? GroupIds);

/// <summary>HR messages: send (e-mail / in-app), inbox and outbox of the caller.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/notifications/messages")]
[Authorize]
public sealed class HrMessagesController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<SendHrMessageResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send([FromBody] SendHrMessageBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new SendHrMessageCommand(body.Subject, body.Body, body.Importance, body.Channels, body.Selection, body.GroupIds), ct));

    [HttpGet("inbox")]
    [ProducesResponseType<HrMessagesPageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inbox(
        [FromQuery] int page = PagingDefaults.FirstPage, [FromQuery] int pageSize = PagingDefaults.DefaultPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListMyInboxQuery(page, pageSize), ct));

    [HttpGet("outbox")]
    [ProducesResponseType<HrMessagesPageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Outbox(
        [FromQuery] int page = PagingDefaults.FirstPage, [FromQuery] int pageSize = PagingDefaults.DefaultPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListMyOutboxQuery(page, pageSize), ct));
}
