using System.Text.Json;
using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.PushDevices;

namespace Sense.Modules.Notifications.Application.Deliveries;

internal static class PushSkipReasons
{
    public const string NoActiveDevice = "no_active_device";
}

/// <summary>
/// Sends one Pending push delivery: reads the user's active devices, calls Expo, updates the entity's status
/// (Sent / Skipped / failure with backoff). Reuses the already-rendered in-app title/body stored on the delivery's
/// model (see NotificationPublisher) - no separate "{key}.push.{lang}.sbn" templates. Never throws for send errors;
/// Expo's DeviceNotRegistered revokes the offending token instead of failing the whole delivery.
/// </summary>
public sealed class PushDeliverySender(
    IPushDeviceTokenRepository devices,
    IExpoPushSender expoSender,
    INotificationTemplateRenderer renderer,
    IOptions<NotificationsOptions> options,
    TimeProvider clock)
{
    public async Task SendAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        var retry = options.Value.Push;
        try
        {
            var userId = Guid.Parse(delivery.RecipientAddress);
            var activeDevices = await devices.ListActiveForUserAsync(userId, ct);
            if (activeDevices.Count == 0)
            {
                delivery.MarkSkipped(PushSkipReasons.NoActiveDevice);
                return;
            }

            var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(delivery.ModelJson) ?? [];
            var model = NotificationPublisher.ToTemplateModel(stored);
            var rendered = renderer.Render(delivery.TemplateKey, NotificationChannels.InApp, delivery.Culture, model);

            var data = new Dictionary<string, string?> { ["type"] = delivery.TemplateKey, ["id"] = delivery.Id.ToString() };
            var messages = activeDevices.Select(d => new ExpoPushMessage(d.Token, rendered.Subject, rendered.Body, data)).ToList();

            var tickets = await expoSender.SendAsync(messages, ct);
            foreach (var ticket in tickets.Where(t => string.Equals(t.ErrorCode, ExpoPushErrorCodes.DeviceNotRegistered, StringComparison.Ordinal)))
            {
                var device = await devices.FindActiveByTokenAsync(ticket.Token, ct);
                device?.Revoke(clock.GetUtcNow().UtcDateTime);
            }

            var otherError = tickets.FirstOrDefault(t => t.IsError && !string.Equals(t.ErrorCode, ExpoPushErrorCodes.DeviceNotRegistered, StringComparison.Ordinal));
            if (otherError is not null)
            {
                delivery.RecordFailure(otherError.ErrorCode ?? "expo_error", clock.GetUtcNow().UtcDateTime, retry.MaxAttempts, retry.BaseBackoffSeconds, retry.MaxBackoffSeconds);
                return;
            }

            delivery.MarkSent(clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.RecordFailure($"{ex.GetType().Name}: {ex.Message}", clock.GetUtcNow().UtcDateTime, retry.MaxAttempts, retry.BaseBackoffSeconds, retry.MaxBackoffSeconds);
        }
    }
}
