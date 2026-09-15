using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Migrator;
using Sense.Modules.DataTransfer.Infrastructure.Persistence;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSenseCore(builder.Configuration);
builder.Services.AddModuleDbContext<IdentityDbContext>(builder.Configuration, IdentityDbContext.SchemaName);
builder.Services.AddModuleDbContext<OrganizationDbContext>(builder.Configuration, OrganizationDbContext.SchemaName);
builder.Services.AddModuleDbContext<EmployeeDbContext>(builder.Configuration, EmployeeDbContext.SchemaName);
builder.Services.AddModuleDbContext<DocumentsDbContext>(builder.Configuration, DocumentsDbContext.SchemaName);
builder.Services.AddModuleDbContext<DataTransferDbContext>(builder.Configuration, DataTransferDbContext.SchemaName);
builder.Services.AddModuleDbContext<WorkflowDbContext>(builder.Configuration, WorkflowDbContext.SchemaName);
builder.Services.AddModuleDbContext<NotificationsDbContext>(builder.Configuration, NotificationsDbContext.SchemaName);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(MigratorConstants.LoggerName);
var command = args.Length > 0 ? args[0].ToLowerInvariant() : MigratorConstants.MigrateCommand;

using var scope = host.Services.CreateScope();
var contexts = scope.ServiceProvider.GetServices<ModuleDbContext>().ToList();

switch (command)
{
    case MigratorConstants.MigrateCommand:
        foreach (var db in contexts)
        {
            logger.LogInformation(MigratorConstants.MigratingMessage, db.Schema);
            await db.Database.MigrateAsync();
        }

        break;

    case MigratorConstants.ResetCommand:
        foreach (var db in contexts)
        {
            logger.LogWarning(MigratorConstants.ResettingMessage, db.Schema);
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        break;

    default:
        logger.LogError(MigratorConstants.UnknownCommandMessage, command);
        return 1;
}

logger.LogInformation(MigratorConstants.DoneMessage, command);
return 0;

namespace Sense.Migrator
{
    internal static class MigratorConstants
    {
        public const string LoggerName = "Sense.Migrator";
        public const string MigrateCommand = "migrate";
        public const string ResetCommand = "reset";
        public const string SeedCommand = "seed";
        public const string MigratingMessage = "Migrating schema {Schema}";
        public const string ResettingMessage = "Resetting database (schema {Schema})";
        public const string UnknownCommandMessage = "Unknown command: {Command}";
        public const string DoneMessage = "Completed: {Command}";
    }
}
