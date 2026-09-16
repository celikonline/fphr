using Microsoft.EntityFrameworkCore;
using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Domain.Webhooks;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Integrations.Infrastructure.Persistence;

/// <summary>EF Core-backed WebhookSubscription repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class WebhookSubscriptionRepository(IntegrationsDbContext context) : IWebhookSubscriptionRepository
{
    public void Add(WebhookSubscription subscription) => context.WebhookSubscriptions.Add(subscription);

    public async Task<Result<WebhookSubscription>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subscription = await context.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return subscription is null ? Error.NotFound(IntegrationsDomainErrors.SubscriptionNotFound) : subscription;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.WebhookSubscriptions.OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WebhookSubscription>> ListSubscribedToAsync(string eventType, CancellationToken cancellationToken = default)
    {
        // Filtered client-side: EventTypes is a small jsonb list per tenant, not something Postgres can index into cheaply here.
        var active = await context.WebhookSubscriptions.Where(s => s.IsActive).ToListAsync(cancellationToken);
        return active.Where(s => s.Subscribes(eventType)).ToList();
    }
}

/// <summary>EF Core-backed WebhookDeliveryLog repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class WebhookDeliveryLogRepository(IntegrationsDbContext context) : IWebhookDeliveryLogRepository
{
    public void Add(WebhookDeliveryLog log) => context.WebhookDeliveries.Add(log);

    public async Task<IReadOnlyList<WebhookDeliveryLog>> ListAsync(Guid? subscriptionId, int take, CancellationToken cancellationToken = default)
    {
        var query = context.WebhookDeliveries.AsQueryable();
        if (subscriptionId is { } id)
        {
            query = query.Where(d => d.SubscriptionId == id);
        }

        return await query.OrderByDescending(d => d.CreatedAt).Take(take).ToListAsync(cancellationToken);
    }
}
