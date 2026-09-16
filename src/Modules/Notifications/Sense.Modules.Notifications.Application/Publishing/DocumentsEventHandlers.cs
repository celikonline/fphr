using System.Globalization;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Modules.Notifications.Contracts;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names for <see cref="NotificationTemplateKeys.DocumentExpiryReminder"/>.</summary>
public static class DocumentsTemplateModel
{
    public const string ExpiringCount = "expiring_count";
    public const string ExpiredCount = "expired_count";

    /// <summary>web/src/pages/settings/missing-documents-report.tsx's "expiring" tab (query string - FE reads it, see faz-01 BE tur 1 report).</summary>
    public const string MissingDocumentsExpiringPath = "/app/settings/company/missing-documents?tab=expiring";
}

/// <summary>
/// documents.expiry_reminder → everyone whose role holds <c>documents.requirements.view</c> (the missing/expiring
/// document report permission), tenant-scoped. <see cref="DocumentExpiryReminderDue"/> only carries tenant-level
/// counts (no per-employee/per-document ids - see the event's own remarks), so a "document owner" recipient
/// cannot be resolved from this event alone; this handler covers the manager/HR-facing digest only (flagged to PO
/// in faz-01 BE tur 1's report as a gap against the literal TALIMAT wording).
/// </summary>
public sealed class DocumentExpiryReminderDueNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<DocumentExpiryReminderDue>
{
    public async Task Handle(DocumentExpiryReminderDue integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.ExpiringCount == 0 && integrationEvent.ExpiredCount == 0)
        {
            return;
        }

        var employeeIds = await recipients.ResolveByPermissionAsync(integrationEvent.TenantId, DocumentsPermissions.RequirementsView.Key, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return;
        }

        var to = await recipients.ResolveAsync(integrationEvent.TenantId, employeeIds, cancellationToken);
        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.DocumentExpiryReminder,
                to,
                new Dictionary<string, string?>
                {
                    [DocumentsTemplateModel.ExpiringCount] = integrationEvent.ExpiringCount.ToString(CultureInfo.InvariantCulture),
                    [DocumentsTemplateModel.ExpiredCount] = integrationEvent.ExpiredCount.ToString(CultureInfo.InvariantCulture),
                },
                DocumentsTemplateModel.MissingDocumentsExpiringPath),
            cancellationToken);
    }
}
