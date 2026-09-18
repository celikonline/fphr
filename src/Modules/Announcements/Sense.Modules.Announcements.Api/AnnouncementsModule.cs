using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Contracts;
using Sense.Modules.Announcements.Infrastructure;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Announcements.Api;

/// <summary>
/// Announcements module composition root: kurumsal duyurular + Support alt alanı ("Yardım Merkezi + Desteğe
/// bağlan", roadmap #69 — 11.7 kararı: ayrı modül değil, Announcements içinde alt alan; şema "announcements",
/// spesifikasyondaki ayrı "support" şeması bu kararla düşer, bkz. 11.3.13).
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

        // Application + Contracts assemblies: Contracts' SupportTicketCreated/Answered integration event types
        // must resolve in OutboxProcessor, see CLAUDE.md's AddModuleHandlers note (Domain raises no domain events
        // here, so it doesn't need its own assembly reference — mirrors AssetsModule.AddModule).
        services.AddModuleHandlers(
            AnnouncementsDbContext.SchemaName,
            typeof(AnnouncementsErrors).Assembly,
            typeof(AnnouncementsDbContext).Assembly,
            typeof(AnnouncementsPermissions).Assembly);

        // Integration event handlers (SupportTicketCreated/Answered -> e-mail) run in Sense.Worker only, which
        // polls the outbox and publishes via IEventBus; the Api host never does (mirrors every other module).
        services.AddAnnouncementsCoreServices(configuration);
    }
}
