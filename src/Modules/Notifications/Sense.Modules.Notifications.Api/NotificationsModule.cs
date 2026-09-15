using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Messaging;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Notifications.Api;

/// <summary>Notifications module composition root: in-app notifications, template catalog, user preferences, publisher port.</summary>
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

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddNotificationsInfrastructure(configuration);

        // Clients.User(userId) needs the JWT "sub" as SignalR user id.
        services.Replace(ServiceDescriptor.Singleton<IUserIdProvider, SubjectClaimUserIdProvider>());
    }
}
