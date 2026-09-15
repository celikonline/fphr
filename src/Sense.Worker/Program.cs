using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Outbox;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSenseCore(builder.Configuration);
builder.Services.AddModuleDbContext<IdentityDbContext>(builder.Configuration, IdentityDbContext.SchemaName);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<IdentityDbContext>>();

// Organization writes integration events (see IIntegrationEventOutbox, e.g. OrgUnitApprovalPolicyChanged) that only
// get delivered once this outbox is polled; EventTypeRegistry must know Organization's Domain+Contracts assemblies
// or OutboxProcessor throws "unknown event type" on the first dequeued message. Identity/Employee/Documents/
// DataTransfer don't write to their outbox yet (nothing calls IIntegrationEventOutbox for them), so wiring them up
// here is deferred until they do - see faz1a-durum-takibi.md for the broader finding (domain events raised via
// Aggregate.Raise(...) are never persisted/dispatched anywhere; this Faz 0 outbox only carries explicit
// IIntegrationEventOutbox writes).
builder.Services.AddModuleDbContext<OrganizationDbContext>(builder.Configuration, OrganizationDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    OrganizationDbContext.SchemaName,
    typeof(OrganizationPermissions).Assembly,
    typeof(OrganizationDbContext).Assembly,
    typeof(IOrgUnitRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<OrganizationDbContext>>();

await builder.Build().RunAsync();

namespace Sense.Worker
{
/// <summary>Faz 0: Hangfire gelene kadar basit polling ile modül outbox'ını işler.</summary>
public sealed class OutboxPollingService<TContext>(IServiceProvider services, IOptions<OutboxOptions> options) : BackgroundService
    where TContext : Sense.Shared.Infrastructure.Persistence.ModuleDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor<TContext>>();
            var processed = await processor.ProcessAsync(stoppingToken);
            if (processed == 0)
            {
                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
}
