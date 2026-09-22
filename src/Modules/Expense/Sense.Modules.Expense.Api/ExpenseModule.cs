using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Expense.Application;
using Sense.Modules.Expense.Contracts;
using Sense.Modules.Expense.Infrastructure;
using Sense.Modules.Expense.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Expense.Api;

/// <summary>Expense module composition root: expense claims (multi-receipt), advance requests, payments overview, categories.</summary>
public sealed class ExpenseModule : IModule
{
    public string Name => ExpenseDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(ExpenseModule).Assembly,
        typeof(ExpenseErrors).Assembly,
        typeof(ExpenseDbContext).Assembly,
        typeof(ExpensePermissions).Assembly,
    ];

    public IEnumerable<Permission> Permissions => ExpensePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates (advance requests, claims, categories) - same wiring as LeaveModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<ExpenseDbContext>(
            configuration, ExpenseDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<ExpenseDbContext>>();

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see WorkflowModule.AddModule's identical comment).
        services.AddModuleHandlers(
            ExpenseDbContext.SchemaName,
            typeof(ExpenseErrors).Assembly,
            typeof(ExpenseDbContext).Assembly,
            typeof(ExpensePermissions).Assembly);

        services.AddExpenseCoreServices();
    }
}
