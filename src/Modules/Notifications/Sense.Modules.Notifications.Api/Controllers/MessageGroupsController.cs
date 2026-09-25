using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Application.HrMessages;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Notifications.Api.Controllers;

public sealed record MessageGroupBody(string Name, EmployeeSelection Selection);

/// <summary>Persistent recipient groups for HR messages.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/notifications/message-groups")]
[Authorize]
public sealed class MessageGroupsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MessageGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMessageGroupsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] MessageGroupBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateMessageGroupCommand(body.Name, body.Selection), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] MessageGroupBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateMessageGroupCommand(id, body.Name, body.Selection), ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteMessageGroupCommand(id), ct));
}
