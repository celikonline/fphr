using System.Reflection;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Notifications.Api;

/// <summary>Notifications module composition root: in-app notifications, e-mail delivery queue, per-user notification settings.</summary>
public sealed class NotificationsModule : IModule
{
    public string Name => NotificationsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(NotificationsModule).Assembly,
        typeof(NotificationsPermissions).Assembly,
        typeof(NotificationsDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => NotificationsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddNotificationsCore(configuration);
}
