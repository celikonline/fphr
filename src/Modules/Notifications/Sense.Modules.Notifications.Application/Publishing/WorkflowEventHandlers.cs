using System.Globalization;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names shared by the Workflow-driven templates and <see cref="ApprovalDecisionLinkEnricher"/>.</summary>
public static class WorkflowTemplateModel
{
    public const string RequestType = "request_type";
    public const string RequesterName = "requester_name";
    public const string StepNumber = "step_number";
    public const string RejectedAtStep = "rejected_at_step";
    public const string ApprovalTaskId = "approval_task_id";

    /// <summary>"true" when the task has a concrete approver (a single-use decision link is minted); "false" for a role queue.</summary>
    public const string IsDirectAssignee = "is_direct_assignee";

    public const string DecisionUrl = "decision_url";
    public const string DecisionExpiresAt = "decision_expires_at";

    /// <summary>Web route of the approval inbox (web/src/App.tsx).</summary>
    public const string InboxPath = "/app/inbox";

    /// <summary>FG-009: display name of whoever commented on a request.</summary>
    public const string AuthorName = "author_name";

    /// <summary>FG-009: app path of one approval request's detail - mobile opens its request detail screen from it.</summary>
    public static string RequestPath(Guid approvalRequestId) => $"/app/requests/{approvalRequestId}";
}

/// <summary>request.created → requester.</summary>
public sealed class ApprovalRequestedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalRequested>
{
    public async Task Handle(ApprovalRequested integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.RequesterEmployeeId], cancellationToken);
        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.RequestCreated,
                to,
                new Dictionary<string, string?> { [WorkflowTemplateModel.RequestType] = integrationEvent.RequestType },
                ActionPath: null),
            cancellationToken);
    }
}

/// <summary>approval.task_assigned → the approver, or every member of the fallback role queue.</summary>
public sealed class ApprovalTaskAssignedNotificationHandler(
    NotificationRecipientResolver recipients, NotificationPublisher publisher, IRoleAssignmentLookup roleAssignments)
    : IIntegrationEventHandler<ApprovalTaskAssigned>
{
    public async Task Handle(ApprovalTaskAssigned integrationEvent, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> approverIds = integrationEvent switch
        {
            { ApproverEmployeeId: { } approverId } => [approverId],
            { FallbackRoleCode: { Length: > 0 } roleCode } =>
                await roleAssignments.ListEmployeeIdsInRoleAsync(integrationEvent.TenantId, roleCode, cancellationToken),
            _ => [],
        };
        if (approverIds.Count == 0)
        {
            return;
        }

        var requester = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.RequesterEmployeeId], cancellationToken);
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, approverIds, cancellationToken);

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.ApprovalTaskAssigned,
                to,
                new Dictionary<string, string?>
                {
                    [WorkflowTemplateModel.RequestType] = integrationEvent.RequestType,
                    [WorkflowTemplateModel.RequesterName] = requester.FirstOrDefault()?.DisplayName,
                    [WorkflowTemplateModel.StepNumber] = integrationEvent.StepNumber.ToString(CultureInfo.InvariantCulture),
                    [WorkflowTemplateModel.ApprovalTaskId] = integrationEvent.ApprovalTaskId.ToString(),
                    [WorkflowTemplateModel.IsDirectAssignee] = integrationEvent.ApproverEmployeeId is null ? "false" : "true",
                },
                WorkflowTemplateModel.InboxPath),
            cancellationToken);
    }
}

/// <summary>request.approved → requester.</summary>
public sealed class ApprovalCompletedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.RequesterEmployeeId], cancellationToken);
        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.RequestApproved,
                to,
                new Dictionary<string, string?> { [WorkflowTemplateModel.RequestType] = integrationEvent.RequestType },
                ActionPath: null),
            cancellationToken);
    }
}

/// <summary>request.rejected → requester.</summary>
public sealed class ApprovalRejectedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.RequesterEmployeeId], cancellationToken);
        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.RequestRejected,
                to,
                new Dictionary<string, string?>
                {
                    [WorkflowTemplateModel.RequestType] = integrationEvent.RequestType,
                    [WorkflowTemplateModel.RejectedAtStep] = integrationEvent.RejectedAtStep.ToString(CultureInfo.InvariantCulture),
                },
                ActionPath: null),
            cancellationToken);
    }
}

/// <summary>Template variable names of workflow.delegation_assigned.</summary>
public static class DelegationTemplateModel
{
    public const string DelegatorName = "delegator_name";
    public const string Period = "period";
}

/// <summary>workflow.delegation_assigned → the delegate (E-mail + in-app): who delegated to them and for which dates.</summary>
public sealed class ApprovalDelegationAssignedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalDelegationAssigned>
{
    private const string DateFormat = "dd.MM.yyyy";

    public async Task Handle(ApprovalDelegationAssigned integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.DelegateEmployeeId], cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        var delegator = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.DelegatorEmployeeId], cancellationToken);
        var period = string.Create(
            CultureInfo.InvariantCulture, $"{integrationEvent.StartDate.ToString(DateFormat, CultureInfo.InvariantCulture)} - {integrationEvent.EndDate.ToString(DateFormat, CultureInfo.InvariantCulture)}");

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.ApprovalDelegationAssigned,
                to,
                new Dictionary<string, string?>
                {
                    [DelegationTemplateModel.DelegatorName] = delegator.FirstOrDefault()?.DisplayName,
                    [DelegationTemplateModel.Period] = period,
                },
                WorkflowTemplateModel.InboxPath),
            cancellationToken);
    }
}

/// <summary>request.comment_added → the request's other parties (recipients resolved by Workflow; the author is never notified).</summary>
public sealed class ApprovalRequestCommentedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<ApprovalRequestCommented>
{
    public async Task Handle(ApprovalRequestCommented integrationEvent, CancellationToken cancellationToken)
    {
        var recipientIds = integrationEvent.RecipientEmployeeIds.Where(id => id != integrationEvent.AuthorEmployeeId).Distinct().ToList();
        if (recipientIds.Count == 0)
        {
            return;
        }

        var author = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.AuthorEmployeeId], cancellationToken);
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, recipientIds, cancellationToken);

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.RequestCommentAdded,
                to,
                new Dictionary<string, string?>
                {
                    [WorkflowTemplateModel.RequestType] = integrationEvent.RequestType,
                    [WorkflowTemplateModel.AuthorName] = author.FirstOrDefault()?.DisplayName,
                },
                WorkflowTemplateModel.RequestPath(integrationEvent.ApprovalRequestId)),
            cancellationToken);
    }
}
