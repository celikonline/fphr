using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Onboarding.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Notifications.Application.Publishing;

/// <summary>
/// Onboarding task templates carry a free-text ResponsibleRoleCode ("hr"/"it"/"manager"/"payroll", ...) rather than a
/// SystemRoleCode - Onboarding.Contracts cannot depend on Identity (modules talk only via *.Contracts). This maps the
/// known codes to the system role whose members receive the notice, same hand-kept-map approach as
/// <see cref="NotificationRecipientResolver"/>'s PermissionRoleCodes. An unrecognized code (a tenant could type
/// anything into the free-text field) resolves to no recipients rather than guessing.
/// </summary>
internal static class OnboardingRoleCodes
{
    private static readonly IReadOnlyDictionary<string, string> SystemRoleCodeByOnboardingCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hr"] = "HR",
            ["it"] = "ITAdmin",
            ["manager"] = "Manager",
            ["payroll"] = "PayrollSpecialist",
        };

    public static string? ToSystemRoleCode(string onboardingRoleCode) =>
        SystemRoleCodeByOnboardingCode.GetValueOrDefault(onboardingRoleCode);
}

/// <summary>Onboarding.OnboardingTaskAssigned → the template's responsible role (hr/it/manager/payroll).</summary>
public sealed class OnboardingTaskAssignedNotificationHandler(
    NotificationRecipientResolver recipients, NotificationPublisher publisher, IRoleAssignmentLookup roleAssignments)
    : IIntegrationEventHandler<OnboardingTaskAssigned>
{
    public Task Handle(OnboardingTaskAssigned integrationEvent, CancellationToken cancellationToken) =>
        OnboardingTaskNotices.PublishAsync(
            recipients, publisher, roleAssignments, NotificationTemplateKeys.OnboardingTaskAssigned,
            integrationEvent.TenantId, integrationEvent.EventId, integrationEvent.ResponsibleRoleCode, integrationEvent.TaskTitle,
            cancellationToken);
}

/// <summary>Onboarding.OnboardingTaskCompleted → the template's responsible role, so it knows the item is closed.</summary>
public sealed class OnboardingTaskCompletedNotificationHandler(
    NotificationRecipientResolver recipients, NotificationPublisher publisher, IRoleAssignmentLookup roleAssignments)
    : IIntegrationEventHandler<OnboardingTaskCompleted>
{
    public Task Handle(OnboardingTaskCompleted integrationEvent, CancellationToken cancellationToken) =>
        OnboardingTaskNotices.PublishAsync(
            recipients, publisher, roleAssignments, NotificationTemplateKeys.OnboardingTaskCompleted,
            integrationEvent.TenantId, integrationEvent.EventId, integrationEvent.ResponsibleRoleCode, integrationEvent.TaskTitle,
            cancellationToken);
}

/// <summary>Shared recipient resolution/publish for the two Onboarding task notices.</summary>
internal static class OnboardingTaskNotices
{
    public const string TaskTitleVariable = "task_title";

    /// <summary>Web route of the onboarding checklist (web/src/App.tsx).</summary>
    private const string ActionPath = "/app/onboarding";

    public static async Task PublishAsync(
        NotificationRecipientResolver recipients,
        NotificationPublisher publisher,
        IRoleAssignmentLookup roleAssignments,
        string templateKey,
        Guid tenantId,
        Guid eventId,
        string responsibleRoleCode,
        string taskTitle,
        CancellationToken cancellationToken)
    {
        if (OnboardingRoleCodes.ToSystemRoleCode(responsibleRoleCode) is not { } systemRoleCode)
        {
            return;
        }

        var employeeIds = await roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, systemRoleCode, cancellationToken);
        if (employeeIds.Count == 0)
        {
            return;
        }

        var to = await recipients.ResolveAsync(tenantId, employeeIds, cancellationToken);
        if (to.Count == 0)
        {
            return;
        }

        await publisher.PublishAsync(
            tenantId,
            new NotificationRequest(eventId, templateKey, to, new Dictionary<string, string?> { [TaskTitleVariable] = taskTitle }, ActionPath),
            cancellationToken);
    }
}
