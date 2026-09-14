using Sense.Modules.Employee.Api;
using Sense.Modules.Identity.Api;
using Sense.Modules.Organization.Api;
using Sense.Shared.Contracts.Modules;

namespace Sense.Api;

/// <summary>Host'un yüklediği modüllerin açık listesi (assembly taraması yerine derleme zamanı görünürlük).</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> Modules { get; } =
    [
        new IdentityModule(),
        new OrganizationModule(),
        new EmployeeModule(),
    ];
}
