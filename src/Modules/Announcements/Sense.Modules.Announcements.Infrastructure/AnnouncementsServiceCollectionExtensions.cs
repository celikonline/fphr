using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Contracts;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Modules.Announcements.Infrastructure.Support;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Infrastructure;

/// <summary>
/// Services Announcements/Support need (repos, unit of work, help article catalog). Used by both the API module
/// (AnnouncementsModule.AddModule) and Sense.Worker (so the outbox poller and Support event handlers resolve),
/// mirroring Assets' AddAssetsCoreServices.
/// </summary>
public static class AnnouncementsServiceCollectionExtensions
{
    public static IServiceCollection AddAnnouncementsCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SupportOptions>().Bind(configuration.GetSection(SupportOptions.Section));

        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IAnnouncementReadRecordRepository, AnnouncementReadRecordRepository>();
        services.AddScoped<IAnnouncementAttachmentRepository, AnnouncementAttachmentRepository>();
        services.AddScoped<ISupportTicketRepository, SupportTicketRepository>();
        services.AddScoped<IHelpSearchLogRepository, HelpSearchLogRepository>();
        services.AddScoped<ICompanyEventRepository, CompanyEventRepository>();
        services.AddScoped<IRecognitionRepository, RecognitionRepository>();
        services.AddScoped<IAnnouncementsUnitOfWork>(sp => sp.GetRequiredService<AnnouncementsDbContext>());
        services.AddScoped<ISupportChannel, EmailSupportChannel>();
        services.AddSingleton<IHelpArticleReader, HelpArticleReader>();
        return services;
    }

    /// <summary>Support's integration event handlers (SupportTicketCreated/Answered -> e-mail), registered explicitly for Sense.Worker.</summary>
    public static IServiceCollection AddAnnouncementsIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<SupportTicketCreated>, SupportTicketCreatedHandler>();
        services.AddScoped<IIntegrationEventHandler<SupportTicketAnswered>, SupportTicketAnsweredHandler>();
        return services;
    }
}
