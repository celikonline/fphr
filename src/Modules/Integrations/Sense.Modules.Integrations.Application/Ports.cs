using Sense.Modules.Integrations.Domain.Webhooks;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Integrations.Application;

/// <summary>Integrations' SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface IIntegrationsUnitOfWork : IUnitOfWork;

/// <summary>Outcome of one outbound delivery attempt.</summary>
public sealed record WebhookDeliveryResult(bool Success, int? StatusCode, string? Error);

/// <summary>Sends a signed HTTP POST to a subscription's URL; implemented in Infrastructure (HttpClient + HMAC-SHA256).</summary>
public interface IWebhookSender
{
    Task<WebhookDeliveryResult> SendAsync(WebhookSubscription subscription, string eventType, object payload, CancellationToken cancellationToken = default);
}
