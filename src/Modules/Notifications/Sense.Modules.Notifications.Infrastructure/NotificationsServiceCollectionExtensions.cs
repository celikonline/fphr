using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Deliveries;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Infrastructure.Delivery;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Modules.Notifications.Infrastructure.Realtime;
using Sense.Modules.Notifications.Infrastructure.Rendering;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;

namespace Sense.Modules.Notifications.Infrastructure;

public static class NotificationsServiceCollectionExtensions
{
    /// <summary>
    /// Everything the module needs in any host: DbContext, handlers (incl. Workflow integration event consumers),
    /// repositories, template renderer, recipient resolution, e-mail sender and delivery processor. Sense.Api gets it via
    /// NotificationsModule; Sense.Worker calls it directly and additionally runs <see cref="EmailDeliveryPollingService"/>.
    /// The host must provide IEmployeeContactLookup, IUserProfileLookup, IRoleAssignmentLookup,
    /// IApprovalDecisionLinkIssuer, IEmployeeLookup, IEmployeeAudienceLookup, IWorkCertificateProvider,
    /// IPayslipDocumentProvider and SignalR (IHubContext&lt;NotificationsHub&gt;).
    /// </summary>
    public static IServiceCollection AddNotificationsCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NotificationsOptions>().Bind(configuration.GetSection(NotificationsOptions.Section));
        services.AddOptions<NotificationsFrontendOptions>().Bind(configuration.GetSection(ConfigurationSections.Frontend));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(ConfigurationSections.Email));

        services.AddModuleDbContext<NotificationsDbContext>(configuration, NotificationsDbContext.SchemaName);

        // Domain + Contracts too: EventTypeRegistry/ModuleUnitOfWorkResolver must know every assembly of the module.
        services.AddModuleHandlers(
            NotificationsDbContext.SchemaName,
            typeof(NotificationsErrors).Assembly,
            typeof(NotificationsDbContext).Assembly,
            typeof(IUserNotificationRepository).Assembly,
            typeof(NotificationsPermissions).Assembly);

        services.AddScoped<IUserNotificationRepository, UserNotificationRepository>();
        services.AddScoped<INotificationDeliveryRepository, NotificationDeliveryRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddSingleton<INotificationTemplateRenderer, ScribanNotificationTemplateRenderer>();
        services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
        services.AddScoped<NotificationRecipientResolver>();
        services.AddScoped<NotificationPublisher>();

        services.AddScoped<IEmailModelEnricher, ApprovalDecisionLinkEnricher>();
        // PDF attachments rendered at send time; need Payroll's IPayslipDocumentProvider and Employee's
        // IWorkCertificateProvider in the host (both hosts register them).
        services.AddScoped<IEmailAttachmentProvider, PayslipAttachmentProvider>();
        services.AddScoped<IEmailAttachmentProvider, WorkCertificateAttachmentProvider>();
        services.AddScoped<EmailDeliverySender>();
        services.AddScoped<EmailDeliveryProcessor>();

        // Same provider switch as IdentityModule (TryAdd: whichever module registers first wins, both pick the same type).
        var emailProvider = configuration.GetSection(ConfigurationSections.Email).Get<EmailOptions>()?.Provider ?? EmailProviders.Smtp;
        if (string.Equals(emailProvider, EmailProviders.None, StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddSingleton<IEmailSender, NullEmailSender>();
        }
        else
        {
            services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
