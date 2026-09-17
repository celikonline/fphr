using Sense.Modules.Integrations.Domain.Webhooks;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Integrations.Domain;

/// <summary>WebhookSubscription aggregate repository.</summary>
public interface IWebhookSubscriptionRepository
{
    void Add(WebhookSubscription subscription);

    Task<Result<WebhookSubscription>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="take"/> subscriptions (API-facing reads); the count is clamped to the shared page-size ceiling.</summary>
    Task<IReadOnlyList<WebhookSubscription>> ListAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Active subscriptions whose <see cref="WebhookSubscription.EventTypes"/> include <paramref name="eventType"/>.</summary>
    Task<IReadOnlyList<WebhookSubscription>> ListSubscribedToAsync(string eventType, CancellationToken cancellationToken = default);
}

/// <summary>WebhookDeliveryLog read/write; append-only, no aggregate invariants beyond creation.</summary>
public interface IWebhookDeliveryLogRepository
{
    void Add(WebhookDeliveryLog log);

    Task<IReadOnlyList<WebhookDeliveryLog>> ListAsync(Guid? subscriptionId, int take, CancellationToken cancellationToken = default);
}
