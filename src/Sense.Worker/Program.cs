using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Outbox;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSenseCore(builder.Configuration);
// Every module's outbox receives both Aggregate.Raise(...) domain events (AuditTenantInterceptor collects them on
// SaveChanges) and explicit IIntegrationEventOutbox writes. EventTypeRegistry must know each polled module's
// Domain+Contracts assemblies or OutboxProcessor retries forever with "Event type could not be resolved".
builder.Services.AddModuleDbContext<IdentityDbContext>(builder.Configuration, IdentityDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    IdentityDbContext.SchemaName,
    typeof(Sense.Modules.Identity.Contracts.AccessSettingKeys).Assembly,
    typeof(IdentityDbContext).Assembly,
    typeof(Sense.Modules.Identity.Domain.IUserRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<IdentityDbContext>>();

builder.Services.AddModuleDbContext<DocumentsDbContext>(builder.Configuration, DocumentsDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    DocumentsDbContext.SchemaName,
    typeof(Sense.Modules.Documents.Contracts.DocumentsPermissions).Assembly,
    typeof(DocumentsDbContext).Assembly,
    typeof(Sense.Modules.Documents.Domain.IDocumentRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<DocumentsDbContext>>();

builder.Services.AddModuleDbContext<OrganizationDbContext>(builder.Configuration, OrganizationDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    OrganizationDbContext.SchemaName,
    typeof(OrganizationPermissions).Assembly,
    typeof(OrganizationDbContext).Assembly,
    typeof(IOrgUnitRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<OrganizationDbContext>>();

// Employee lifecycle integration events (EmployeeHired/Suspended/Reactivated) consumed by Leave/Workflow.
builder.Services.AddModuleDbContext<EmployeeDbContext>(builder.Configuration, EmployeeDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    EmployeeDbContext.SchemaName,
    typeof(EmployeePermissions).Assembly,
    typeof(EmployeeDbContext).Assembly,
    typeof(IEmployeeRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<EmployeeDbContext>>();

// Workflow writes ApprovalCompleted/ApprovalRejected to its own outbox (see IIntegrationEventOutbox usages in
// Sense.Modules.Workflow.Application's decision handlers) - see the EventTypeRegistry note at the top.
builder.Services.AddModuleDbContext<WorkflowDbContext>(builder.Configuration, WorkflowDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    WorkflowDbContext.SchemaName,
    typeof(WorkflowPermissions).Assembly,
    typeof(WorkflowDbContext).Assembly,
    typeof(IApprovalPolicyRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<WorkflowDbContext>>();

// Notifications consumes Workflow's ApprovalRequestCreated/ApprovalTaskAssigned/ApprovalCompleted/ApprovalRejected and needs
// Employee.Contracts.IEmployeeDirectory to resolve requester user id/e-mail. Only GetContactsAsync is used here, which
// doesn't touch the selection resolver (whose data-scope chain the Worker doesn't host).
builder.Services.AddNotificationsInfrastructure(builder.Configuration);
builder.Services.AddScoped<IEmployeeDirectory>(sp =>
    new Sense.Modules.Employee.Infrastructure.Lookups.EmployeeDirectory(sp.GetRequiredService<EmployeeDbContext>(), null!));
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<Sense.Modules.Notifications.Infrastructure.Persistence.NotificationsDbContext>>();

// Notifications created here reach browsers through the API's SignalR Redis backplane (same channel prefix as
// Sense.Shared.Web's AddSignalR). Without Redis no IHubContext is registered and the push is skipped (clients poll).
var signalRRedis = builder.Configuration.GetConnectionString(ConnectionStringNames.Redis);
if (!string.IsNullOrWhiteSpace(signalRRedis))
{
    builder.Services.AddSignalR().AddStackExchangeRedis(signalRRedis, options =>
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("senseik-signalr-"));
}

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
