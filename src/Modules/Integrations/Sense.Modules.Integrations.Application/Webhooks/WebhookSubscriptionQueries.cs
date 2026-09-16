using Sense.Modules.Integrations.Contracts;
using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Domain.Webhooks;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Integrations.Application.Webhooks;

/// <summary>Lists the tenant's webhook subscriptions (never includes the secret).</summary>
[RequiresPermission(IntegrationsPermissions.WebhooksViewKey)]
public sealed record ListWebhookSubscriptionsQuery : IQuery<IReadOnlyList<WebhookSubscriptionDto>>;

public sealed class ListWebhookSubscriptionsHandler(IWebhookSubscriptionRepository subscriptions)
    : IQueryHandler<ListWebhookSubscriptionsQuery, IReadOnlyList<WebhookSubscriptionDto>>
{
    public async Task<Result<IReadOnlyList<WebhookSubscriptionDto>>> Handle(ListWebhookSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        var list = await subscriptions.ListAsync(cancellationToken);
        IReadOnlyList<WebhookSubscriptionDto> dtos = list.Select(ToDto).ToList();
        return Result.Success(dtos);
    }

    private static WebhookSubscriptionDto ToDto(WebhookSubscription subscription) =>
        new(subscription.Id, subscription.Url, subscription.EventTypes, subscription.IsActive);
}

/// <summary>Lists recent delivery attempts, optionally filtered to one subscription.</summary>
[RequiresPermission(IntegrationsPermissions.WebhooksDeliveriesKey)]
public sealed record ListWebhookDeliveriesQuery(Guid? SubscriptionId, int Take = 50) : IQuery<IReadOnlyList<WebhookDeliveryDto>>;

public sealed class ListWebhookDeliveriesHandler(IWebhookDeliveryLogRepository deliveries)
    : IQueryHandler<ListWebhookDeliveriesQuery, IReadOnlyList<WebhookDeliveryDto>>
{
    public async Task<Result<IReadOnlyList<WebhookDeliveryDto>>> Handle(ListWebhookDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var list = await deliveries.ListAsync(query.SubscriptionId, query.Take, cancellationToken);
        IReadOnlyList<WebhookDeliveryDto> dtos = list
            .Select(d => new WebhookDeliveryDto(d.Id, d.SubscriptionId, d.EventType, d.Success, d.StatusCode, d.Error, d.CreatedAt))
            .ToList();
        return Result.Success(dtos);
    }
}
