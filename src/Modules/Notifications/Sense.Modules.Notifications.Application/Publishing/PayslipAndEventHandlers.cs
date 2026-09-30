using System.Globalization;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Documents.Contracts.Events;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Payroll.Contracts;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names and web paths for the payslip follow-up notices and the event invitation.</summary>
public static class PayslipAndEventTemplateModel
{
    public const string EventTitle = "event_title";
    public const string EmployeeName = "employee_name";

    /// <summary>web/src/pages/company-calendar.tsx.</summary>
    public const string CompanyCalendarPath = "/app/company-calendar";

    public static string PayrollTabPath(Guid employeeId) => string.Create(CultureInfo.InvariantCulture, $"/app/employees/{employeeId}/payroll");

    public static string DocumentsTabPath(Guid employeeId) => string.Create(CultureInfo.InvariantCulture, $"/app/employees/{employeeId}/documents");

    public static string Period(int year, int month) => string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");
}

/// <summary>Announcements.CompanyEventInvitationRequested → announcement.event_invitation to the listed invitees only.</summary>
public sealed class CompanyEventInvitationNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<CompanyEventInvitationRequested>
{
    public async Task Handle(CompanyEventInvitationRequested integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, integrationEvent.EmployeeIds, cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.CompanyEventInvitation,
                to,
                new Dictionary<string, string?> { [PayslipAndEventTemplateModel.EventTitle] = integrationEvent.Title },
                PayslipAndEventTemplateModel.CompanyCalendarPath),
            cancellationToken);
    }
}

/// <summary>Payroll.PayslipAcknowledgementReminderDue → payroll.payslip_acknowledgement_reminder, one notice per unacknowledged payslip's employee.</summary>
public sealed class PayslipAcknowledgementReminderNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<PayslipAcknowledgementReminderDue>
{
    public async Task Handle(PayslipAcknowledgementReminderDue integrationEvent, CancellationToken cancellationToken)
    {
        var tenantId = integrationEvent.TenantId;
        var resolved = (await recipients.ResolveAsync(tenantId, integrationEvent.Payslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(r => r.EmployeeId);

        foreach (var payslip in integrationEvent.Payslips)
        {
            if (!resolved.TryGetValue(payslip.EmployeeId, out var recipient))
            {
                continue;
            }

            await publisher.PublishAsync(
                tenantId,
                new NotificationRequest(
                    PerPeriodSourceId(integrationEvent.EventId, payslip.PeriodId),
                    NotificationTemplateKeys.PayslipAcknowledgementReminder,
                    [recipient],
                    new Dictionary<string, string?>
                    {
                        [AnnouncementPayrollTemplateModel.PayslipId] = payslip.PayslipId.ToString(),
                        [AnnouncementPayrollTemplateModel.Period] = PayslipAndEventTemplateModel.Period(payslip.Year, payslip.Month),
                    },
                    PayslipAndEventTemplateModel.PayrollTabPath(payslip.EmployeeId)),
                cancellationToken);
        }
    }

    /// <summary>
    /// The dedup key is (SourceEventId, template, user): one employee with payslips for two periods in the same event would lose the
    /// second notice. A stable id per (event, period) keeps redelivery idempotent while separating periods (N1).
    /// </summary>
    internal static Guid PerPeriodSourceId(Guid eventId, Guid periodId)
    {
        Span<byte> bytes = stackalloc byte[32];
        eventId.TryWriteBytes(bytes[..16]);
        periodId.TryWriteBytes(bytes[16..]);
        return new Guid(System.Security.Cryptography.SHA256.HashData(bytes)[..16]);
    }
}

/// <summary>Payroll.PayslipObjectionAccepted → payroll.payslip_objection_accepted to the members of the event's target role (Bordro Uzmanı).</summary>
public sealed class PayslipObjectionAcceptedNotificationHandler(
    NotificationRecipientResolver recipients, NotificationPublisher publisher, IRoleAssignmentLookup roleAssignments)
    : IIntegrationEventHandler<PayslipObjectionAccepted>
{
    public async Task Handle(PayslipObjectionAccepted integrationEvent, CancellationToken cancellationToken)
    {
        var tenantId = integrationEvent.TenantId;
        var memberIds = await roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, integrationEvent.NotifyRoleCode, cancellationToken);
        if (memberIds.Count == 0)
        {
            return;
        }

        var to = await recipients.ResolveAsync(tenantId, memberIds, cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        var owner = (await recipients.ResolveAsync(tenantId, [integrationEvent.EmployeeId], cancellationToken)).FirstOrDefault();
        await publisher.PublishAsync(
            tenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.PayslipObjectionAccepted,
                to,
                new Dictionary<string, string?>
                {
                    [PayslipAndEventTemplateModel.EmployeeName] = owner?.DisplayName,
                    [AnnouncementPayrollTemplateModel.Period] = PayslipAndEventTemplateModel.Period(integrationEvent.Year, integrationEvent.Month),
                },
                PayslipAndEventTemplateModel.PayrollTabPath(integrationEvent.EmployeeId)),
            cancellationToken);
    }
}

/// <summary>Documents.PayslipDocumentUploaded → payroll.payslip_document_uploaded to the matched employee.</summary>
public sealed class PayslipDocumentUploadedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<PayslipDocumentUploaded>
{
    public async Task Handle(PayslipDocumentUploaded integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, [integrationEvent.EmployeeId], cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.PayslipDocumentUploaded,
                to,
                new Dictionary<string, string?>
                {
                    [AnnouncementPayrollTemplateModel.Period] = PayslipAndEventTemplateModel.Period(integrationEvent.Year, integrationEvent.Month),
                },
                PayslipAndEventTemplateModel.DocumentsTabPath(integrationEvent.EmployeeId)),
            cancellationToken);
    }
}
