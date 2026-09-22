using System.Globalization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Notifications.Contracts;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names and helpers for the employee.* templates.</summary>
public static class EmployeeTemplateModel
{
    public const string EmployeeId = "employee_id";
    public const string EmployeeName = "employee_name";
    public const string HireDate = "hire_date";
    public const string TerminationDate = "termination_date";

    /// <summary>
    /// Hire/exit notices are only sent for dates within this many days before today (future dates always): a data import
    /// or a back-dated correction must not flood HR with notices about long-past events.
    /// </summary>
    public const int RecentEventWindowDays = 30;

    public static string EmployeeCardPath(Guid employeeId) => $"/app/employees/{employeeId}";

    public static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static bool IsRecent(DateOnly date, DateOnly today) => date >= today.AddDays(-RecentEventWindowDays);
}

/// <summary>
/// EmployeeHired → employee.hired to HR (employee.employees.update holders) and the new employee's manager, and
/// employee.welcome to the new employee (work e-mail, else personal e-mail).
/// </summary>
public sealed class EmployeeHiredNotificationHandler(
    NotificationRecipientResolver recipients,
    NotificationPublisher publisher,
    IEmployeeContactLookup contacts,
    IEmployeeLookup employees,
    TimeProvider clock) : IIntegrationEventHandler<EmployeeHired>
{
    public async Task Handle(EmployeeHired integrationEvent, CancellationToken cancellationToken)
    {
        var tenantId = integrationEvent.TenantId;
        if (!EmployeeTemplateModel.IsRecent(integrationEvent.HireDate, Today()))
        {
            return;
        }

        var name = await EmployeeNotices.DisplayNameAsync(contacts, tenantId, integrationEvent.EmployeeId, cancellationToken);
        var hireDate = EmployeeTemplateModel.FormatDate(integrationEvent.HireDate);

        var hr = await EmployeeNotices.HrAndManagerAsync(recipients, employees, tenantId, integrationEvent.EmployeeId, cancellationToken);
        await publisher.PublishAsync(
            tenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.EmployeeHired,
                hr,
                new Dictionary<string, string?>
                {
                    [EmployeeTemplateModel.EmployeeName] = name,
                    [EmployeeTemplateModel.HireDate] = hireDate,
                },
                EmployeeTemplateModel.EmployeeCardPath(integrationEvent.EmployeeId)),
            cancellationToken);

        var newcomer = await recipients.ResolveAsync(tenantId, [integrationEvent.EmployeeId], cancellationToken);
        await publisher.PublishAsync(
            tenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.EmployeeWelcome,
                newcomer,
                new Dictionary<string, string?> { [EmployeeTemplateModel.HireDate] = hireDate },
                null),
            cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

/// <summary>
/// EmployeeTerminated → employee.terminated to HR and the manager, and employee.work_certificate (çalışma belgesi PDF,
/// 4857 md.28) to the leaver's personal e-mail. No personal e-mail on record → no certificate mail (HR can still
/// download it from the employee card).
/// </summary>
public sealed class EmployeeTerminatedNotificationHandler(
    NotificationRecipientResolver recipients,
    NotificationPublisher publisher,
    IEmployeeContactLookup contacts,
    IEmployeeLookup employees,
    TimeProvider clock) : IIntegrationEventHandler<EmployeeTerminated>
{
    public async Task Handle(EmployeeTerminated integrationEvent, CancellationToken cancellationToken)
    {
        var tenantId = integrationEvent.TenantId;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var terminationDate = integrationEvent.TerminationDate ?? today;
        if (!EmployeeTemplateModel.IsRecent(terminationDate, today))
        {
            return;
        }

        var name = await EmployeeNotices.DisplayNameAsync(contacts, tenantId, integrationEvent.EmployeeId, cancellationToken);
        var model = new Dictionary<string, string?>
        {
            [EmployeeTemplateModel.EmployeeId] = integrationEvent.EmployeeId.ToString(),
            [EmployeeTemplateModel.EmployeeName] = name,
            [EmployeeTemplateModel.TerminationDate] = EmployeeTemplateModel.FormatDate(terminationDate),
        };

        var hr = await EmployeeNotices.HrAndManagerAsync(recipients, employees, tenantId, integrationEvent.EmployeeId, cancellationToken);
        await publisher.PublishAsync(
            tenantId,
            new NotificationRequest(
                integrationEvent.EventId, NotificationTemplateKeys.EmployeeTerminated, hr, model,
                EmployeeTemplateModel.EmployeeCardPath(integrationEvent.EmployeeId)),
            cancellationToken);

        if (await recipients.ResolvePersonalAddressAsync(tenantId, integrationEvent.EmployeeId, cancellationToken) is { } leaver)
        {
            await publisher.PublishAsync(
                tenantId,
                new NotificationRequest(integrationEvent.EventId, NotificationTemplateKeys.EmployeeWorkCertificate, [leaver], model, null),
                cancellationToken);
        }
    }
}

/// <summary>EmployeeBirthdaysToday → employee.birthday to each celebrating employee.</summary>
public sealed class EmployeeBirthdayNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<EmployeeBirthdaysToday>
{
    public async Task Handle(EmployeeBirthdaysToday integrationEvent, CancellationToken cancellationToken)
    {
        var to = await recipients.ResolveAsync(integrationEvent.TenantId, integrationEvent.EmployeeIds, cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(integrationEvent.EventId, NotificationTemplateKeys.EmployeeBirthday, to, new Dictionary<string, string?>(), null),
            cancellationToken);
    }
}

/// <summary>Shared recipient/name resolution for the hire and exit notices.</summary>
internal static class EmployeeNotices
{
    private const string HrPermissionKey = EmployeePermissions.EmployeesUpdateKey;

    public static async Task<string> DisplayNameAsync(IEmployeeContactLookup contacts, Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var contact = (await contacts.GetContactsAsync(tenantId, [employeeId], ct)).FirstOrDefault();
        return contact is null ? string.Empty : $"{contact.FirstName} {contact.LastName}".Trim();
    }

    /// <summary>HR (by permission) plus the employee's direct manager, never the employee themself.</summary>
    public static async Task<IReadOnlyList<NotificationRecipient>> HrAndManagerAsync(
        NotificationRecipientResolver recipients, IEmployeeLookup employees, Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var ids = new HashSet<Guid>(await recipients.ResolveByPermissionAsync(tenantId, HrPermissionKey, ct));
        if (await employees.GetManagerEmployeeIdAsync(tenantId, employeeId, ct) is { } managerId)
        {
            ids.Add(managerId);
        }

        ids.Remove(employeeId);
        return await recipients.ResolveAsync(tenantId, ids, ct);
    }
}
