using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Contracts;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Announcements.Api;

/// <summary>Announcements (Duyurular) module composition root.</summary>
public sealed class AnnouncementsModule : IModule
{
    public string Name => AnnouncementsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(AnnouncementsModule).Assembly,
        typeof(AnnouncementsPermissions).Assembly,
        typeof(AnnouncementsDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => AnnouncementsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AnnouncementsDbContext>(configuration, AnnouncementsDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        services.AddModuleHandlers(
            AnnouncementsDbContext.SchemaName,
            typeof(AnnouncementsErrors).Assembly,
            typeof(AnnouncementsDbContext).Assembly,
            typeof(AnnouncementsPermissions).Assembly);

        services.AddScoped<IAnnouncementsUnitOfWork>(sp => sp.GetRequiredService<AnnouncementsDbContext>());
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IAnnouncementReadRecordRepository, AnnouncementReadRecordRepository>();
    }
}
