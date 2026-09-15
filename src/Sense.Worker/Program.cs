using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Delivery;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Modules.Performance.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Infrastructure.Messaging;
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

// Notifications (B2) consumes the Workflow events above (request created/approved/rejected, task assigned): writes
// in-app notifications + pushes them over SignalR (Redis backplane → Api connections), queues e-mail deliveries and
// sends them with EmailDeliveryPollingService. Its handlers need these cross-module lookups, which resolve here
// with just the modules' DbContexts registered above.
var redisConnection = builder.Configuration.GetConnectionString(ConnectionStringNames.Redis);
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("senseik-signalr-"));
}

builder.Services.AddScoped<IEmployeeContactLookup, EmployeeContactLookup>();
builder.Services.AddScoped<IUserProfileLookup, UserProfileLookup>();
builder.Services.AddScoped<IRoleAssignmentLookup, RoleAssignmentLookup>();
builder.Services.AddWorkflowDecisionLinks(builder.Configuration);
builder.Services.AddNotificationsCore(builder.Configuration);
builder.Services.AddHostedService<EmailDeliveryPollingService>();
// Leave consumes EmployeeHired (leave profile + default rule set + accruals) and Workflow's ApprovalCompleted/Rejected
// (leave request decisions); OutboxProcessor publishes through this process's IEventBus, so the handlers live here.
// Handlers are registered explicitly (not the whole Application assembly) so API-only handler dependencies stay out.
builder.Services.AddModuleDbContext<Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext>(builder.Configuration, Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext.SchemaName);
builder.Services.AddLeaveCoreServices(builder.Configuration);
builder.Services.AddLeaveIntegrationEventHandlers();
builder.Services.AddModuleHandlers(
    Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext.SchemaName,
    typeof(Sense.Modules.Leave.Contracts.LeavePermissions).Assembly,
    typeof(Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext).Assembly,
    typeof(Sense.Modules.Leave.Domain.ILeaveRuleSetRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<Sense.Modules.Leave.Infrastructure.Persistence.LeaveDbContext>>();
builder.Services.AddLeaveBalanceRecalculationJob();

// Performance writes ReviewCyclePublished/ReviewParticipantCompleted to its own outbox (Publish/ForceComplete/
// SubmitReviewResponse handlers) - no cross-module consumer yet (see plan deviation, Notifications wiring is a
// follow-up), but the outbox still needs polling so messages don't pile up unprocessed.
builder.Services.AddModuleDbContext<PerformanceDbContext>(builder.Configuration, PerformanceDbContext.SchemaName);
builder.Services.AddModuleHandlers(
    PerformanceDbContext.SchemaName,
    typeof(PerformancePermissions).Assembly,
    typeof(PerformanceDbContext).Assembly,
    typeof(IReviewCycleRepository).Assembly);
builder.Services.AddHostedService<Sense.Worker.OutboxPollingService<PerformanceDbContext>>();

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
