using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Infrastructure.Lookups;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Employee.Api;

/// <summary>Employee module composition root (Faz 0 minimal stub: Id/UserId/DisplayName/Status/OrgUnitId only - full personnel record is Faz 1).</summary>
public sealed class EmployeeModule : IModule
{
    public string Name => EmployeeDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(EmployeeModule).Assembly,
        typeof(EmployeePermissions).Assembly,
        typeof(EmployeeDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => EmployeePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Register DbContext for this module
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);

        // Register MediatR-style command/query handlers for this module
        services.AddModuleHandlers(EmployeeDbContext.SchemaName, typeof(EmployeePermissions).Assembly, typeof(EmployeeDbContext).Assembly);

        // Register repositories
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();

        // Cross-module read contract consumed by Organization (see IEmployeeLookup)
        services.AddScoped<IEmployeeLookup, EmployeeLookup>();
    }
}
