using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Organization.Api;

/// <summary>Organization modülü kompozisyon kökü (Faz 0: iskelet; şirket/birim/lokasyon/pozisyon bir sonraki adımda).</summary>
public sealed class OrganizationModule : IModule
{
    public const string ModuleName = "org";

    public string Name => ModuleName;

    public IReadOnlyList<Assembly> Assemblies { get; } = [typeof(OrganizationModule).Assembly];

    public IEnumerable<Permission> Permissions => [];

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
