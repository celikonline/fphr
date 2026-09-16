using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Domain.Webhooks;

namespace Sense.Modules.Integrations.Application.Webhooks;

/// <summary>
/// Delivers one event to every active subscription that wants it and logs the outcome. Never throws: a failing
/// delivery must not fail the originating module's outbox message (see EmployeeLifecycleWebhookHandlers).
/// </summary>
public sealed class WebhookDispatchService(
    IWebhookSubscriptionRepository subscriptions, IWebhookDeliveryLogRepository deliveries, IWebhookSender sender, IIntegrationsUnitOfWork unitOfWork)
{
    public async Task DispatchAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        var targets = await subscriptions.ListSubscribedToAsync(eventType, cancellationToken);
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var subscription in targets)
        {
            var result = await sender.SendAsync(subscription, eventType, payload, cancellationToken);
            deliveries.Add(WebhookDeliveryLog.Record(subscription.TenantId, subscription.Id, eventType, result.Success, result.StatusCode, result.Error));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
