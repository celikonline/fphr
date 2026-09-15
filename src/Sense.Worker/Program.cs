using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Modules.Leave.Infrastructure;
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
