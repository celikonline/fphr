using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Integrations.Application.Webhooks;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Integrations.Api.Controllers;

/// <summary>Outbound webhook delivery log (read-only; "integrations.webhooks.deliveries", already on the Auditor role).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/integrations/webhooks/deliveries")]
[Authorize]
public sealed class WebhookDeliveriesController : ApiControllerBase
{
    /// <summary>Lists recent delivery attempts, optionally filtered to one subscription.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WebhookDeliveryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? subscriptionId, [FromQuery] int take, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(take > 0 ? new ListWebhookDeliveriesQuery(subscriptionId, take) : new ListWebhookDeliveriesQuery(subscriptionId), ct));
}
