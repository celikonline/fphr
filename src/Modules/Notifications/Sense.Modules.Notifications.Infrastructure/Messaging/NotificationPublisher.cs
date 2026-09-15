using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Notifications;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Notifications;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Infrastructure.Messaging;

/// <summary>Minimal local binding of the shared "Frontend" section (BaseUrl only) for absolute e-mail links.</summary>
public sealed class NotificationsFrontendOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// INotificationPublisher: applies catalog defaults + user preferences, persists in-app rows (own SaveChanges),
/// pushes them over SignalR and sends e-mails via IEmailSender. E-mail and push text use the default culture
/// (no per-user language yet); the in-app list re-renders in the reader's culture.
/// </summary>
public sealed partial class NotificationPublisher(
    NotificationsDbContext db,
    INotificationRepository notifications,
    INotificationPreferenceRepository preferences,
    ResourceNotificationTemplateRenderer renderer,
    INotificationRealtimePusher pusher,
    IEmailSender emailSender,
    ITenantContext tenantContext,
    ITenantContextSetter tenantContextSetter,
    TimeProvider clock,
    IOptions<LocalizationOptions> localization,
    IOptions<NotificationsFrontendOptions> frontend,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    public async Task<Result<NotificationPublishResult>> PublishAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var definition = NotificationTemplateCatalog.Find(request.TemplateKey);
        if (definition is null)
        {
            return Error.Validation(NotificationsErrors.UnknownTemplate, ("TemplateKey", request.TemplateKey));
        }

        if (tenantContext.IsResolved && tenantContext.TenantId != request.TenantId)
        {
            return Error.Forbidden(ErrorCodes.Forbidden);
        }

        using var tenantScope = tenantContext.IsResolved ? null : tenantContextSetter.BeginScope(request.TenantId);

        var recipients = request.Recipients
            .Where(r => r.UserId is not null || !string.IsNullOrWhiteSpace(r.Email))
            .DistinctBy(r => (object?)r.UserId ?? r.Email!.ToUpperInvariant())
            .ToList();
        var userIds = recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList();
        var overrides = userIds.Count == 0
            ? new Dictionary<Guid, Domain.Preferences.NotificationPreference>()
            : (await preferences.ListForUsersAsync(userIds, definition.Key, ct)).ToDictionary(p => p.UserId);

        var now = clock.GetUtcNow().UtcDateTime;
        var argsJson = NotificationArgs.Serialize(request.Args);
        var created = new List<Notification>();
        var mailTo = new List<NotificationRecipient>();
        var skipped = 0;

        foreach (var recipient in recipients)
        {
            var pref = recipient.UserId is { } uid ? overrides.GetValueOrDefault(uid) : null;
            var inApp = request.Channels.HasFlag(NotificationChannels.InApp) && recipient.UserId is not null
                && (definition.IsMandatory || (pref?.InAppEnabled ?? definition.DefaultChannels.HasFlag(NotificationChannels.InApp)));
            var email = request.Channels.HasFlag(NotificationChannels.Email) && !string.IsNullOrWhiteSpace(recipient.Email)
                && (definition.IsMandatory || (pref?.EmailEnabled ?? definition.DefaultChannels.HasFlag(NotificationChannels.Email)));

            if (inApp)
            {
                var notification = Notification.Create(request.TenantId, recipient.UserId!.Value, definition.Key, definition.Category, argsJson, request.Link, now);
                if (notification.IsFailure)
                {
                    return notification.Error;
                }

                notifications.Add(notification.Value);
                created.Add(notification.Value);
            }

            if (email)
            {
                mailTo.Add(recipient);
            }

            if (!inApp && !email)
            {
                skipped++;
            }
        }

        if (created.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var culture = CultureInfo.GetCultureInfo(localization.Value.DefaultCulture);
        foreach (var notification in created)
        {
            try
            {
                await pusher.PushAsync(notification.RecipientUserId, NotificationMapper.ToDto(notification, renderer, culture), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.PushFailed(logger, ex, notification.Id);
            }
        }

        var sent = 0;
        foreach (var recipient in mailTo)
        {
            try
            {
                await emailSender.SendAsync(BuildEmail(recipient, request, culture), ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.EmailFailed(logger, ex, definition.Key);
            }
        }

        return new NotificationPublishResult(created.Count, sent, skipped);
    }

    private EmailMessage BuildEmail(NotificationRecipient recipient, NotificationRequest request, CultureInfo culture)
    {
        var args = new Dictionary<string, string>(request.Args, StringComparer.Ordinal)
        {
            [NotificationTemplateArgs.RecipientName] = recipient.DisplayName ?? recipient.Email!,
        };
        var rendered = renderer.Render(request.TemplateKey, args, culture);
        var parts = new List<string> { renderer.RenderFragment(ResourceNotificationTemplateRenderer.EmailGreetingKey, args, culture), rendered.Body };
        if (!string.IsNullOrWhiteSpace(request.Link))
        {
            args[ResourceNotificationTemplateRenderer.UrlArg] = string.Concat(frontend.Value.BaseUrl.TrimEnd('/'), request.Link);
            parts.Add(renderer.RenderFragment(ResourceNotificationTemplateRenderer.EmailOpenLinkKey, args, culture));
        }

        return new EmailMessage(recipient.Email!, recipient.DisplayName, rendered.Title, string.Join(Environment.NewLine + Environment.NewLine, parts));
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4300, Level = LogLevel.Warning, Message = "Real-time push failed for notification {NotificationId}")]
        public static partial void PushFailed(ILogger logger, Exception exception, Guid notificationId);

        [LoggerMessage(EventId = 4301, Level = LogLevel.Error, Message = "Notification e-mail failed for template {TemplateKey}")]
        public static partial void EmailFailed(ILogger logger, Exception exception, string templateKey);
    }
}
