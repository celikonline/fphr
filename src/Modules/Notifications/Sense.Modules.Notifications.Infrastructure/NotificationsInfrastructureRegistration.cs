using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Infrastructure.Messaging;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Notifications;

namespace Sense.Modules.Notifications.Infrastructure;

/// <summary>Shared by the API module and Sense.Worker (which hosts the integration event consumers).</summary>
public static class NotificationsInfrastructureRegistration
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotificationsDbContext>(configuration, NotificationsDbContext.SchemaName);
        services.AddModuleHandlers(
            NotificationsDbContext.SchemaName,
            typeof(NotificationsErrors).Assembly,
            typeof(NotificationsDbContext).Assembly,
            typeof(INotificationRepository).Assembly,
            typeof(NotificationsPermissions).Assembly);

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.TryAddSingleton<ResourceNotificationTemplateRenderer>();
        services.TryAddSingleton<INotificationTemplateRenderer>(sp => sp.GetRequiredService<ResourceNotificationTemplateRenderer>());
        services.TryAddSingleton<INotificationRealtimePusher, SignalRNotificationRealtimePusher>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddOptions<NotificationsFrontendOptions>().Bind(configuration.GetSection(ConfigurationSections.Frontend));

        // Same provider selection as IdentityModule (TryAdd: whichever registers first wins; Worker has no Identity module).
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(ConfigurationSections.Email));
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
