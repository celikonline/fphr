using System.Globalization;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.OccupationalSafety.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names for <see cref="NotificationTemplateKeys.OhsSgkNotificationDue"/>.</summary>
public static class OhsTemplateModel
{
    public const string DueSoonCount = "due_soon_count";
    public const string OverdueCount = "overdue_count";

    /// <summary>web/src/pages/ohs.tsx - the incident register with the SGK column.</summary>
    public const string IncidentsPath = "/app/ohs";

    /// <summary>OhsPermissions.IncidentsManage (Notifications must not reference the OHS module beyond its Contracts events).</summary>
    public const string IncidentsManagePermission = "ohs.incidents.manage";
}

/// <summary>ohs.sgk_notification_due -> everyone whose role holds ohs.incidents.manage, tenant-scoped (same digest shape as documents.expiry_reminder).</summary>
public sealed class SgkNotificationReminderDueNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<SgkNotificationReminderDue>
{
    public async Task Handle(SgkNotificationReminderDue integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.DueSoonCount == 0 && integrationEvent.OverdueCount == 0)
        {
            return;
        }

        var employeeIds = await recipients.ResolveByPermissionAsync(
            integrationEvent.TenantId, OhsTemplateModel.IncidentsManagePermission, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return;
        }

        var to = await recipients.ResolveAsync(integrationEvent.TenantId, employeeIds, cancellationToken);
        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.OhsSgkNotificationDue,
                to,
                new Dictionary<string, string?>
                {
                    [OhsTemplateModel.DueSoonCount] = integrationEvent.DueSoonCount.ToString(CultureInfo.InvariantCulture),
                    [OhsTemplateModel.OverdueCount] = integrationEvent.OverdueCount.ToString(CultureInfo.InvariantCulture),
                },
                OhsTemplateModel.IncidentsPath),
            cancellationToken);
    }
}
