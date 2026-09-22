using System.Globalization;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Application.Deliveries;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Payroll.Contracts;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names for announcement.published and payroll.payslip_ready.</summary>
public static class AnnouncementPayrollTemplateModel
{
    public const string AnnouncementTitle = "announcement_title";
    public const string AnnouncementExcerpt = "announcement_excerpt";

    /// <summary>"true" for High/Urgent announcements (subject gets a marker).</summary>
    public const string IsHighPriority = "is_high_priority";

    public const string AnnouncementsPath = "/app/announcements";

    public const string PayslipId = "payslip_id";

    /// <summary>"2026-09" style period label.</summary>
    public const string Period = "period";
}

/// <summary>AnnouncementPublished → announcement.published to every active (or suspended) employee.</summary>
public sealed class AnnouncementPublishedNotificationHandler(
    NotificationRecipientResolver recipients,
    NotificationPublisher publisher,
    IEmployeeAudienceLookup audience) : IIntegrationEventHandler<AnnouncementPublished>
{
    public async Task Handle(AnnouncementPublished integrationEvent, CancellationToken cancellationToken)
    {
        var employeeIds = await audience.ListNotifiableEmployeeIdsAsync(integrationEvent.TenantId, cancellationToken);
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, employeeIds, cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.AnnouncementPublished,
                to,
                new Dictionary<string, string?>
                {
                    [AnnouncementPayrollTemplateModel.AnnouncementTitle] = integrationEvent.Title,
                    [AnnouncementPayrollTemplateModel.AnnouncementExcerpt] = integrationEvent.Excerpt,
                    [AnnouncementPayrollTemplateModel.IsHighPriority] = integrationEvent.IsHighPriority ? "true" : "false",
                },
                AnnouncementPayrollTemplateModel.AnnouncementsPath),
            cancellationToken);
    }
}

/// <summary>
/// PayrollPeriodPaid → payroll.payslip_ready to each employee with their own payslip (PDF attached at send time by
/// <see cref="PayslipAttachmentProvider"/>). Employees who left during the period get it at their personal e-mail.
/// </summary>
public sealed class PayrollPeriodPaidNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<PayrollPeriodPaid>
{
    public async Task Handle(PayrollPeriodPaid integrationEvent, CancellationToken cancellationToken)
    {
        var tenantId = integrationEvent.TenantId;
        var resolved = (await recipients.ResolveAsync(tenantId, integrationEvent.Payslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(r => r.EmployeeId);
        var period = string.Create(CultureInfo.InvariantCulture, $"{integrationEvent.Year:D4}-{integrationEvent.Month:D2}");

        foreach (var payslip in integrationEvent.Payslips)
        {
            var recipient = resolved.GetValueOrDefault(payslip.EmployeeId)
                ?? await recipients.ResolvePersonalAddressAsync(tenantId, payslip.EmployeeId, cancellationToken);
            if (recipient is null)
            {
                continue;
            }

            await publisher.PublishAsync(
                tenantId,
                new NotificationRequest(
                    integrationEvent.EventId,
                    NotificationTemplateKeys.PayslipReady,
                    [recipient],
                    new Dictionary<string, string?>
                    {
                        [AnnouncementPayrollTemplateModel.PayslipId] = payslip.PayslipId.ToString(),
                        [AnnouncementPayrollTemplateModel.Period] = period,
                    },
                    null),
                cancellationToken);
        }
    }
}

/// <summary>Attaches the ücret pusulası PDF to payroll.payslip_ready e-mails; skips when the payslip is gone.</summary>
public sealed class PayslipAttachmentProvider(IPayslipDocumentProvider documents) : IEmailAttachmentProvider
{
    public const string PayslipNotFound = "payslip no longer exists";

    public string TemplateKey => NotificationTemplateKeys.PayslipReady;

    public async Task<EmailAttachmentResult> GetAsync(Guid tenantId, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default)
    {
        if (!Guid.TryParse(model.GetValueOrDefault(AnnouncementPayrollTemplateModel.PayslipId) as string, out var payslipId)
            || await documents.RenderPdfAsync(tenantId, payslipId, ct) is not { } file)
        {
            return EmailAttachmentResult.SkipWith(PayslipNotFound);
        }

        return EmailAttachmentResult.With(new EmailAttachment(file.FileName, file.ContentType, file.Content));
    }
}

/// <summary>Attaches the çalışma belgesi PDF to employee.work_certificate e-mails; skips when the employee was re-hired meanwhile.</summary>
public sealed class WorkCertificateAttachmentProvider(IWorkCertificateProvider certificates) : IEmailAttachmentProvider
{
    public const string NotTerminated = "employee is no longer terminated";

    public string TemplateKey => NotificationTemplateKeys.EmployeeWorkCertificate;

    public async Task<EmailAttachmentResult> GetAsync(Guid tenantId, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default)
    {
        if (!Guid.TryParse(model.GetValueOrDefault(EmployeeTemplateModel.EmployeeId) as string, out var employeeId)
            || await certificates.RenderPdfAsync(tenantId, employeeId, ct) is not { } file)
        {
            return EmailAttachmentResult.SkipWith(NotTerminated);
        }

        return EmailAttachmentResult.With(new EmailAttachment(file.FileName, file.ContentType, file.Content));
    }
}
