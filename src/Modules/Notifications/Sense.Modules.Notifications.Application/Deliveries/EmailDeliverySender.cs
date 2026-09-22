using System.Text.Json;
using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application.Publishing;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Modules.Notifications.Application.Deliveries;

/// <summary>
/// Sends one Pending e-mail delivery: template-specific enrichment, render in the delivery's culture, IEmailSender.
/// Updates the entity's status (Sent / Skipped / failure with backoff); the caller saves. Never throws for send errors.
/// </summary>
public sealed class EmailDeliverySender(
    IEnumerable<IEmailModelEnricher> enrichers,
    IEnumerable<IEmailAttachmentProvider> attachmentProviders,
    INotificationTemplateRenderer renderer,
    IEmailSender emailSender,
    IOptions<NotificationsOptions> options,
    TimeProvider clock)
{
    public async Task SendAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        var retry = options.Value.Delivery;
        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(delivery.ModelJson) ?? [];
            var model = NotificationPublisher.ToTemplateModel(stored);

            foreach (var enricher in enrichers.Where(e => string.Equals(e.TemplateKey, delivery.TemplateKey, StringComparison.Ordinal)))
            {
                var enriched = await enricher.EnrichAsync(delivery.TenantId, model, ct);
                if (enriched.Skip)
                {
                    delivery.MarkSkipped(enriched.SkipReason ?? string.Empty);
                    return;
                }
            }

            var attachments = new List<EmailAttachment>();
            foreach (var provider in attachmentProviders.Where(p => string.Equals(p.TemplateKey, delivery.TemplateKey, StringComparison.Ordinal)))
            {
                var result = await provider.GetAsync(delivery.TenantId, model, ct);
                if (result.SkipReason is not null)
                {
                    delivery.MarkSkipped(result.SkipReason);
                    return;
                }

                attachments.AddRange(result.Attachments);
            }

            var rendered = renderer.Render(delivery.TemplateKey, NotificationChannels.Email, delivery.Culture, model);
            await emailSender.SendAsync(
                new EmailMessage(delivery.RecipientAddress, delivery.RecipientName, rendered.Subject, rendered.Body, rendered.HtmlBody,
                    attachments.Count == 0 ? null : attachments), ct);
            delivery.MarkSent(clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.RecordFailure($"{ex.GetType().Name}: {ex.Message}", clock.GetUtcNow().UtcDateTime, retry.MaxAttempts, retry.BaseBackoffSeconds, retry.MaxBackoffSeconds);
        }
    }
}

/// <summary>
/// approval.task_assigned e-mails to a concrete approver get a single-use decision link minted right now; if the task
/// was decided (or moved on) since the event, the e-mail is skipped. Role-queue e-mails only link to the inbox.
/// </summary>
public sealed class ApprovalDecisionLinkEnricher(IApprovalDecisionLinkIssuer linkIssuer) : IEmailModelEnricher
{
    public const string TaskNoLongerActionable = "approval task is no longer the pending current step";

    public string TemplateKey => NotificationTemplateKeys.ApprovalTaskAssigned;

    public async Task<EnrichmentResult> EnrichAsync(Guid tenantId, Dictionary<string, object?> model, CancellationToken ct = default)
    {
        if (!Equals(model.GetValueOrDefault(WorkflowTemplateModel.IsDirectAssignee), "true")
            || !Guid.TryParse(model.GetValueOrDefault(WorkflowTemplateModel.ApprovalTaskId) as string, out var taskId))
        {
            return EnrichmentResult.Continue;
        }

        var link = await linkIssuer.IssueAsync(tenantId, taskId, ct);
        if (link is null)
        {
            return EnrichmentResult.SkipWith(TaskNoLongerActionable);
        }

        model[WorkflowTemplateModel.DecisionUrl] = link.Url;
        model[WorkflowTemplateModel.DecisionExpiresAt] = link.ExpiresAtUtc;
        return EnrichmentResult.Continue;
    }
}
