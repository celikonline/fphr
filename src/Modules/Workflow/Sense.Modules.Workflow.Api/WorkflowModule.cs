using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Infrastructure.Lookups;
using Sense.Modules.Workflow.Infrastructure.Messaging;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Modules.Workflow.Infrastructure.Resolvers;
using Sense.Modules.Workflow.Infrastructure.Security;
using Sense.Modules.Workflow.Infrastructure.Services;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Workflow.Api;

/// <summary>Workflow module composition root: approval policies, approval requests/tasks, e-mail decisions, inbox.</summary>
public sealed class WorkflowModule : IModule
{
    public string Name => WorkflowDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(WorkflowModule).Assembly,
        typeof(WorkflowPermissions).Assembly,
        typeof(WorkflowDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => WorkflowPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<WorkflowDbContext>(configuration, WorkflowDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor,
        // and Contracts events (ApprovalCompleted/ApprovalRejected) map to this module's outbox (IIntegrationEventOutbox).
        services.AddModuleHandlers(
            WorkflowDbContext.SchemaName,
            typeof(WorkflowErrors).Assembly,
            typeof(WorkflowDbContext).Assembly,
            typeof(IApprovalPolicyRepository).Assembly,
            typeof(WorkflowPermissions).Assembly);

        services.AddScoped<IApprovalPolicyRepository, ApprovalPolicyRepository>();
        services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();
        services.AddScoped<IEmailDecisionTokenRepository, EmailDecisionTokenRepository>();

        services.AddScoped<IApproverResolver, DefaultApproverResolver>();
        services.AddScoped<IEmailTokenGenerator, EmailTokenGenerator>();
        services.AddScoped<IApprovalMailer, ApprovalMailer>();
        services.AddScoped<IWorkflowFrontendLinks, WorkflowFrontendLinks>();
        services.AddOptions<WorkflowFrontendOptions>().Bind(configuration.GetSection(ConfigurationSections.Frontend));

        // Cross-module read/write ports consumed by other modules (future Leave, Expense, ...)
        services.AddScoped<IApprovalRequestLookup, ApprovalRequestLookup>();
        services.AddScoped<IWorkflowService, WorkflowService>();
    }
}
