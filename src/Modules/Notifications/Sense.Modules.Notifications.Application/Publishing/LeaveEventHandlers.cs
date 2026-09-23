using System.Globalization;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Notifications.Contracts;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>Template variable names for the leave.* templates.</summary>
public static class LeaveTemplateModel
{
    public const string LeaveTypeName = "leave_type_name";
    public const string StartDate = "start_date";
    public const string EndDate = "end_date";
    public const string Days = "days";

    public static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string FormatDays(decimal days) => days.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>TK-014: LeaveAddedOnBehalf → leave.added_on_behalf to the employee (manager/HR added leave on their behalf).</summary>
public sealed class LeaveAddedOnBehalfNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<LeaveAddedOnBehalf>
{
    public async Task Handle(LeaveAddedOnBehalf integrationEvent, CancellationToken cancellationToken)
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
                NotificationTemplateKeys.LeaveAddedOnBehalf,
                to,
                new Dictionary<string, string?>
                {
                    [LeaveTemplateModel.LeaveTypeName] = integrationEvent.LeaveTypeName,
                    [LeaveTemplateModel.StartDate] = LeaveTemplateModel.FormatDate(integrationEvent.StartDate),
                    [LeaveTemplateModel.EndDate] = LeaveTemplateModel.FormatDate(integrationEvent.EndDate),
                    [LeaveTemplateModel.Days] = LeaveTemplateModel.FormatDays(integrationEvent.Days),
                },
                ActionPath: null),
            cancellationToken);
    }
}
