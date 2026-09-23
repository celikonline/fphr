using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Onboarding.Contracts.Events;

/// <summary>
/// Raised once an onboarding/offboarding checklist task is materialized for an employee. ResponsibleRoleCode is the
/// template's free-text role code (e.g. "hr", "it", "manager", "payroll") - not a specific person (11.7 #74).
/// Notifications resolves it to that tenant's role members and fans the notice out to them.
/// </summary>
public sealed record OnboardingTaskAssigned(
    Guid TenantId, Guid TaskId, Guid EmployeeId, string ResponsibleRoleCode, string TaskTitle) : IntegrationEvent(TenantId);

/// <summary>Raised when a checklist task is completed, so its responsible role knows the item is closed.</summary>
public sealed record OnboardingTaskCompleted(
    Guid TenantId, Guid TaskId, Guid EmployeeId, string ResponsibleRoleCode, string TaskTitle) : IntegrationEvent(TenantId);
