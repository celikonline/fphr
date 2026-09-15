using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.EventHandlers;

/// <summary>
/// Requester-facing approval outcome notifications (#43). Workflow's ApprovalMailer only e-mails approvers, so the
/// requester gets both in-app and e-mail here (subject to preferences) - no double send.
/// </summary>
public sealed class ApprovalCompletedNotificationHandler(IEmployeeDirectory employees, INotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken) =>
        RequesterNotifier.NotifyAsync(
            employees, publisher, integrationEvent.TenantId, integrationEvent.RequesterEmployeeId, integrationEvent.RequestType,
            NotificationTemplateKeys.RequestApproved, cancellationToken);
}

public sealed class ApprovalRejectedNotificationHandler(IEmployeeDirectory employees, INotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken) =>
        RequesterNotifier.NotifyAsync(
            employees, publisher, integrationEvent.TenantId, integrationEvent.RequesterEmployeeId, integrationEvent.RequestType,
            NotificationTemplateKeys.RequestRejected, cancellationToken);
}

/// <summary>"Talep oluşturuldu" confirmation to the requester (in-app + e-mail).</summary>
public sealed class ApprovalRequestCreatedNotificationHandler(IEmployeeDirectory employees, INotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalRequestCreated>
{
    public Task Handle(ApprovalRequestCreated integrationEvent, CancellationToken cancellationToken) =>
        RequesterNotifier.NotifyAsync(
            employees, publisher, integrationEvent.TenantId, integrationEvent.RequesterEmployeeId, integrationEvent.RequestType,
            NotificationTemplateKeys.RequestCreated, cancellationToken);
}

/// <summary>
/// "Onayınız bekleniyor" in-app notification for a concrete approver. In-app only: Workflow's ApprovalMailer already sends
/// the decision e-mail. Fallback role queues (no assignee) are not fanned out yet.
/// </summary>
public sealed class ApprovalTaskAssignedNotificationHandler(IEmployeeDirectory employees, INotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalTaskAssigned>
{
    public async Task Handle(ApprovalTaskAssigned integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.ApproverEmployeeId is not { } approverEmployeeId)
        {
            return;
        }

        var contacts = await employees.GetContactsAsync(
            integrationEvent.TenantId, [approverEmployeeId, integrationEvent.RequesterEmployeeId], cancellationToken);
        var approver = contacts.FirstOrDefault(c => c.EmployeeId == approverEmployeeId);
        if (approver?.UserId is null)
        {
            return;
        }

        var requester = contacts.FirstOrDefault(c => c.EmployeeId == integrationEvent.RequesterEmployeeId);
        var args = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationTemplateArgs.RequestType] = integrationEvent.RequestType,
            [NotificationTemplateArgs.RequesterName] = requester is null ? string.Empty : $"{requester.FirstName} {requester.LastName}".Trim(),
        };

        await publisher.PublishAsync(
            new NotificationRequest(
                integrationEvent.TenantId,
                [new NotificationRecipient(approver.UserId, null, $"{approver.FirstName} {approver.LastName}".Trim())],
                NotificationTemplateKeys.ApprovalTaskAssigned,
                args,
                NotificationLinks.Inbox,
                NotificationChannels.InApp),
            cancellationToken);
    }
}

internal static class RequesterNotifier
{
    public static async Task NotifyAsync(
        IEmployeeDirectory employees, INotificationPublisher publisher, Guid tenantId, Guid requesterEmployeeId, string requestType,
        string templateKey, CancellationToken ct)
    {
        var contacts = await employees.GetContactsAsync(tenantId, [requesterEmployeeId], ct);
        if (contacts.Count == 0)
        {
            return;
        }

        var contact = contacts[0];
        var name = $"{contact.FirstName} {contact.LastName}".Trim();
        var recipient = new NotificationRecipient(contact.UserId, contact.WorkEmail ?? contact.PersonalEmail, name);
        var args = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationTemplateArgs.RequestType] = requestType,
            [NotificationTemplateArgs.RequesterName] = name,
        };

        await publisher.PublishAsync(new NotificationRequest(tenantId, [recipient], templateKey, args, NotificationLinks.MyRequests), ct);
    }
}
