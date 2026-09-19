using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Contracts;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Announcements.Api;

/// <summary>
/// Announcements module composition root: company announcements + Event category (home "Etkinlik takvimi" widget) and
/// the Support sub-area (platform Help Center articles, "Desteğe bağlan" tickets). Per 11.7 "Modül adayları", Support
/// lives here rather than in a separate Sense.Modules.Support module.
/// </summary>
public sealed class AnnouncementsModule : IModule
{
    public string Name => AnnouncementsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(AnnouncementsModule).Assembly,
        typeof(AnnouncementsErrors).Assembly,
        typeof(AnnouncementsDbContext).Assembly,
        typeof(AnnouncementsPermissions).Assembly,
    ];

    public IEnumerable<Permission> Permissions => AnnouncementsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AnnouncementsDbContext>(configuration, AnnouncementsDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see ExpenseModule.AddModule's identical comment).
        services.AddModuleHandlers(
            AnnouncementsDbContext.SchemaName,
            typeof(AnnouncementsErrors).Assembly,
            typeof(AnnouncementsDbContext).Assembly,
            typeof(IAnnouncementRepository).Assembly,
            typeof(AnnouncementsPermissions).Assembly);

        services.AddAnnouncementsCoreServices(configuration);
    }
}
