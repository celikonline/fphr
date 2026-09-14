using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Api;

/// <summary>Employee modülü kompozisyon kökü (Faz 0: iskelet; özlük Faz 1'de).</summary>
public sealed class EmployeeModule : IModule
{
    public const string ModuleName = "employee";

    public string Name => ModuleName;

    public IReadOnlyList<Assembly> Assemblies { get; } = [typeof(EmployeeModule).Assembly];

    public IEnumerable<Permission> Permissions => [];

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
