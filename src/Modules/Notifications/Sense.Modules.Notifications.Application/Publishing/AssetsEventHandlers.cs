using System.Globalization;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Notifications.Contracts;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>
/// Template variable names and helpers for the assets.* templates. The Assets events carry ids and dates only (no
/// asset name), so the templates point to the assets screen for the details instead of naming the equipment.
/// </summary>
public static class AssetsTemplateModel
{
    public const string IssuedAt = "issued_at";
    public const string ReturnedAt = "returned_at";
    public const string EmployeeName = "employee_name";

    /// <summary>web/src/pages/assets.tsx - "Zimmetlerim" for employees, the inventory for assets.assets.view holders.</summary>
    public const string AssetsPath = "/app/assets";

    public static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

/// <summary>AssetAssigned → assets.assignment_created to the employee the equipment was assigned to.</summary>
public sealed class AssetAssignedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<AssetAssigned>
{
    public async Task Handle(AssetAssigned integrationEvent, CancellationToken cancellationToken)
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
                NotificationTemplateKeys.AssetAssigned,
                to,
                new Dictionary<string, string?>
                {
                    [AssetsTemplateModel.IssuedAt] = AssetsTemplateModel.FormatDate(integrationEvent.IssuedAt),
                },
                AssetsTemplateModel.AssetsPath),
            cancellationToken);
    }
}

/// <summary>AssetReturned → assets.assignment_returned to the employee who returned the equipment (confirmation it was recorded).</summary>
public sealed class AssetReturnedNotificationHandler(NotificationRecipientResolver recipients, NotificationPublisher publisher)
    : IIntegrationEventHandler<AssetReturned>
{
    public async Task Handle(AssetReturned integrationEvent, CancellationToken cancellationToken)
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
                NotificationTemplateKeys.AssetReturned,
                to,
                new Dictionary<string, string?>
                {
                    [AssetsTemplateModel.ReturnedAt] = AssetsTemplateModel.FormatDate(integrationEvent.ReturnedAt),
                },
                AssetsTemplateModel.AssetsPath),
            cancellationToken);
    }
}

/// <summary>
/// AssetUnreturnedAtTermination → assets.unreturned_at_termination to holders of assets.assets.manage
/// (Owner/HR/PayrollSpecialist fallback of <see cref="NotificationRecipientResolver.ResolveByPermissionAsync"/>),
/// one notification per still-open asset. The leaver's name comes straight from IEmployeeContactLookup because
/// ResolveAsync skips terminated employees.
/// </summary>
public sealed class AssetUnreturnedAtTerminationNotificationHandler(
    NotificationRecipientResolver recipients, NotificationPublisher publisher, IEmployeeContactLookup contacts)
    : IIntegrationEventHandler<AssetUnreturnedAtTermination>
{
    public async Task Handle(AssetUnreturnedAtTermination integrationEvent, CancellationToken cancellationToken)
    {
        var employeeIds = await recipients.ResolveByPermissionAsync(integrationEvent.TenantId, AssetsPermissions.AssetsManage.Key, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return;
        }

        var to = await recipients.ResolveAsync(integrationEvent.TenantId, employeeIds, cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        var leaver = (await contacts.GetContactsAsync(integrationEvent.TenantId, [integrationEvent.EmployeeId], cancellationToken))
            .FirstOrDefault();
        var leaverName = leaver is null ? string.Empty : $"{leaver.FirstName} {leaver.LastName}".Trim();

        await publisher.PublishAsync(
            integrationEvent.TenantId,
            new NotificationRequest(
                integrationEvent.EventId,
                NotificationTemplateKeys.AssetUnreturnedAtTermination,
                to,
                new Dictionary<string, string?>
                {
                    [AssetsTemplateModel.EmployeeName] = leaverName,
                },
                AssetsTemplateModel.AssetsPath),
            cancellationToken);
    }
}
