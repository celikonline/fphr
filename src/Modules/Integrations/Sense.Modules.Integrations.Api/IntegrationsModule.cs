using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Integrations.Application.Webhooks;
using Sense.Modules.Integrations.Contracts;
using Sense.Modules.Integrations.Domain;
using Sense.Modules.Integrations.Infrastructure;
using Sense.Modules.Integrations.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Integrations.Api;

/// <summary>Integrations module composition root (Faz 8 #61: outbound webhooks, see faz8-durum-takibi.md).</summary>
public sealed class IntegrationsModule : IModule
{
    public string Name => IntegrationsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(IntegrationsModule).Assembly,
        typeof(IntegrationsPermissions).Assembly,
        typeof(IntegrationsDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => IntegrationsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IntegrationsDbContext>(configuration, IntegrationsDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see CLAUDE.md's AddModuleHandlers note - Contracts-assembly-only registration silently registers nothing).
        services.AddModuleHandlers(
            IntegrationsDbContext.SchemaName,
            typeof(WebhookDispatchService).Assembly,
            typeof(IntegrationsDbContext).Assembly,
            typeof(IWebhookSubscriptionRepository).Assembly,
            typeof(IntegrationsPermissions).Assembly);

        services.AddIntegrationsServices();
    }
}
