using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sense.Modules.Notifications.Application.Catalog;
using Sense.Modules.Notifications.Application.UserNotifications;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.UserNotifications;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>What to notify: one template, its variables (snake_case, string values), and who receives it. <paramref name="Channels"/> (optional) narrows delivery to the listed channels.</summary>
public sealed record NotificationRequest(
    Guid SourceEventId,
    string TemplateKey,
    IReadOnlyList<NotificationRecipient> Recipients,
    IReadOnlyDictionary<string, string?> Model,
    string? ActionPath,
    IReadOnlyCollection<string>? Channels = null);

/// <summary>
/// Turns a notification request into rows: an in-app <see cref="UserNotification"/> (rendered now, pushed over
/// SignalR after save) and a Pending e-mail <see cref="NotificationDelivery"/> (rendered and sent later by the delivery
/// processor) per recipient, honouring the catalog defaults and user preferences. Does no external I/O before the
/// save, and skips rows that already exist for the same source event, so a re-delivered integration event is harmless.
/// </summary>
public sealed partial class NotificationPublisher(
    IUserNotificationRepository notifications,
    INotificationDeliveryRepository deliveries,
    INotificationPreferenceRepository preferences,
    INotificationsUnitOfWork unitOfWork,
    INotificationTemplateRenderer renderer,
    IRealtimeNotifier realtime,
    TimeProvider clock,
    ILogger<NotificationPublisher> logger)
{
    public const string RecipientNameKey = "recipient_name";
    public const string ActionPathKey = "action_path";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(Guid tenantId, NotificationRequest request, CancellationToken ct)
    {
        var definition = NotificationCatalog.Find(request.TemplateKey)
            ?? throw new InvalidOperationException($"Notification template '{request.TemplateKey}' is not in the catalog.");

        var userIds = request.Recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        IReadOnlyList<Domain.Preferences.NotificationPreference> userPreferences = userIds.Count == 0
            ? []
            : await preferences.ListForUsersAsync(userIds, request.TemplateKey, ct);

        bool Enabled(Guid? userId, string channel)
        {
            if (request.Channels is not null && !request.Channels.Contains(channel, StringComparer.Ordinal))
            {
                return false;
            }

            var explicitChoice = userId is { } uid
                ? userPreferences.FirstOrDefault(p => p.UserId == uid && p.Channel == channel)?.Enabled
                : null;
            return NotificationCatalog.IsEnabled(definition, channel, explicitChoice);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var created = new List<UserNotification>();
        var queuedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipient in request.Recipients)
        {
            var model = BuildModel(request, recipient);

            if (recipient.UserId is { } userId && Enabled(userId, NotificationChannels.InApp)
                && created.TrueForAll(n => n.UserId != userId)
                && !await notifications.ExistsAsync(request.SourceEventId, request.TemplateKey, userId, ct))
            {
                var rendered = renderer.Render(request.TemplateKey, NotificationChannels.InApp, recipient.Language, ToTemplateModel(model));
                var notification = new UserNotification(tenantId, userId, request.TemplateKey, rendered.Subject, rendered.Body, request.ActionPath, request.SourceEventId);
                notifications.Add(notification);
                created.Add(notification);
            }

            if (recipient.Email is { } email && recipient.AddressSource is { } source && Enabled(recipient.UserId, NotificationChannels.Email)
                && queuedAddresses.Add(email)
                && !await deliveries.ExistsAsync(request.SourceEventId, request.TemplateKey, NotificationChannels.Email, email, ct))
            {
                deliveries.Add(new NotificationDelivery(
                    tenantId, request.SourceEventId, request.TemplateKey, NotificationChannels.Email, recipient.EmployeeId, recipient.UserId,
                    email, recipient.DisplayName, source, recipient.Language, JsonSerializer.Serialize(model, JsonOptions), now));
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var notification in created)
        {
            await realtime.NotifyUserAsync(notification.UserId, UserNotificationDto.From(notification), ct);
        }

        Log.Published(logger, request.TemplateKey, request.SourceEventId, created.Count, queuedAddresses.Count);
    }

    /// <summary>JSON-stored string model → template model (plain object values).</summary>
    public static Dictionary<string, object?> ToTemplateModel(IReadOnlyDictionary<string, string?> model) =>
        model.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);

    private static Dictionary<string, string?> BuildModel(NotificationRequest request, NotificationRecipient recipient)
    {
        var model = new Dictionary<string, string?>(request.Model, StringComparer.Ordinal)
        {
            [RecipientNameKey] = recipient.DisplayName,
        };
        if (request.ActionPath is not null)
        {
            model[ActionPathKey] = request.ActionPath;
        }

        return model;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4100, Level = LogLevel.Debug, Message = "Notification {TemplateKey} for event {SourceEventId}: {InAppCount} in-app created, {EmailCount} e-mail recipients")]
        public static partial void Published(ILogger logger, string templateKey, Guid sourceEventId, int inAppCount, int emailCount);
    }
}
