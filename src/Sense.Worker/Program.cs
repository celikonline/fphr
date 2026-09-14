using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Outbox;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSenseCore(builder.Configuration);
builder.Services.AddModuleDbContext<IdentityDbContext>(builder.Configuration, IdentityDbContext.SchemaName);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<IdentityDbContext>>();

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
