using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Integrations.Application.Webhooks;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Integrations.Api.Controllers;

/// <summary>Request to register a webhook subscription.</summary>
public sealed record RegisterWebhookSubscriptionRequest(string Url, IReadOnlyList<string> EventTypes);

/// <summary>Outbound webhook subscriptions (Faz 8 #61: dış bordro yazılımı senkronu, "Bordro modülü kapalıyken employee.* event senkronu").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/integrations/webhooks")]
[Authorize]
public sealed class WebhookSubscriptionsController : ApiControllerBase
{
    /// <summary>Lists the tenant's webhook subscriptions (never includes the secret).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WebhookSubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListWebhookSubscriptionsQuery(take), ct));

    /// <summary>Registers a subscription; the response is the only time the caller sees the signing secret.</summary>
    [HttpPost]
    [ProducesResponseType<WebhookSubscriptionCreatedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterWebhookSubscriptionRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RegisterWebhookSubscriptionCommand(request.Url, request.EventTypes), ct));

    /// <summary>Stops deliveries to a subscription without deleting its history.</summary>
    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new DeactivateWebhookSubscriptionCommand(id), ct));

    /// <summary>Resumes deliveries to a previously deactivated subscription.</summary>
    [HttpPost(ApiRoutes.IdParam + "/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) => FromResult(await Dispatcher.Send(new ReactivateWebhookSubscriptionCommand(id), ct));
}
