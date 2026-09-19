using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Modules.Announcements.Infrastructure.Support;

namespace Sense.Modules.Announcements.Infrastructure;

public static class AnnouncementsServiceCollectionExtensions
{
    /// <summary>
    /// Services Announcements needs (repositories, unit of work, SupportOptions, the e-mail ISupportChannel). Used by the
    /// API module (AnnouncementsModule.AddModule) and Sense.Worker (outbox poller for SupportTicketCreated), mirroring
    /// Assets' AddAssetsCoreServices. The e-mail adapter relies on the host's IEmailSender (registered by Identity/
    /// Notifications). No consumed integration events yet, so there is no matching event-handlers half.
    /// </summary>
    public static IServiceCollection AddAnnouncementsCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SupportOptions>().Bind(configuration.GetSection(SupportOptions.Section));

        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IHelpCategoryRepository, HelpCategoryRepository>();
        services.AddScoped<IHelpArticleRepository, HelpArticleRepository>();
        services.AddScoped<ISupportTicketRepository, SupportTicketRepository>();
        services.AddScoped<IAnnouncementsUnitOfWork>(sp => sp.GetRequiredService<AnnouncementsDbContext>());
        services.AddScoped<ISupportChannel, EmailSupportChannel>();
        return services;
    }
}
