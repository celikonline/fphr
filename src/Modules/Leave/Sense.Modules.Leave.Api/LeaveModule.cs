using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Infrastructure;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Leave.Api;

/// <summary>Leave module composition root: rule sets/types, ledger + balances, LegislationParameter reader (ADR 0008).</summary>
public sealed class LeaveModule : IModule
{
    public string Name => LeaveDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(LeaveModule).Assembly,
        typeof(LeavePermissions).Assembly,
        typeof(LeaveDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => LeavePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<LeaveDbContext>(configuration, LeaveDbContext.SchemaName);
        services.AddModuleHandlers(
            LeaveDbContext.SchemaName,
            typeof(LeaveErrors).Assembly,
            typeof(LeaveDbContext).Assembly,
            typeof(ILeaveRuleSetRepository).Assembly,
            typeof(LeavePermissions).Assembly);
        services.AddLeaveServices(configuration);
    }
}
