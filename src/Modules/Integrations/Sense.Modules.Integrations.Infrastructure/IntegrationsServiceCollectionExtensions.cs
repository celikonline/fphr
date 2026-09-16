using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Integrations.Application;
using Sense.Modules.Integrations.Application.Webhooks;
using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Infrastructure.Persistence;
using Sense.Modules.Integrations.Infrastructure.Webhooks;

namespace Sense.Modules.Integrations.Infrastructure;

public static class IntegrationsServiceCollectionExtensions
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddIntegrationsServices(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationsUnitOfWork>(sp => sp.GetRequiredService<IntegrationsDbContext>());
        services.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionRepository>();
        services.AddScoped<IWebhookDeliveryLogRepository, WebhookDeliveryLogRepository>();
        services.AddScoped<WebhookDispatchService>();
        services.AddScoped<IWebhookSender, HttpWebhookSender>();
        services.AddHttpClient(WebhookHttpClientNames.Default, c => c.Timeout = SendTimeout);
        return services;
    }
}
