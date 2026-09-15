using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Application.ImportJobs;
using Sense.Modules.DataTransfer.Contracts;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Modules.DataTransfer.Infrastructure.Files;
using Sense.Modules.DataTransfer.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.DataTransfer.Api;

/// <summary>
/// DataTransfer module composition root (Faz 0 items 7/8: "Veri aktarım merkezi" import core). Templates and row handlers are
/// contributed by other modules through Sense.Shared.Contracts.Import (IImportTemplate/IImportRowHandler, e.g. Employee's
/// "employee.master"). Faz 0 storage decision: uploaded source files are stored as bytea rows in the "datatransfer" schema
/// (IFileStorage only offers presigned URLs); result workbooks are generated on demand from the source file and the stored row
/// outcomes, localized by ?lang=. Runs are synchronous (no Hangfire/SFTP/export yet).
/// </summary>
public sealed class DataTransferModule : IModule
{
    public string Name => DataTransferDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(DataTransferModule).Assembly,
        typeof(DataTransferPermissions).Assembly,
        typeof(DataTransferDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => DataTransferPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DataTransferDbContext>(configuration, DataTransferDbContext.SchemaName);

        // Handlers/validators live in the Application assembly (not Contracts).
        services.AddModuleHandlers(DataTransferDbContext.SchemaName, typeof(DataTransferErrors).Assembly, typeof(DataTransferDbContext).Assembly);

        services.AddScoped<ImportJobRepository>();
        services.AddScoped<IImportJobRepository>(sp => sp.GetRequiredService<ImportJobRepository>());
        services.AddScoped<IImportJobReadStore>(sp => sp.GetRequiredService<ImportJobRepository>());
        services.AddScoped<ImportTemplateCatalog>();
        services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();
        services.AddSingleton<IImportWorkbookWriter, ImportWorkbookWriter>();
        services.AddSingleton<IImportLocalizer, ResourceImportLocalizer>();
    }
}
