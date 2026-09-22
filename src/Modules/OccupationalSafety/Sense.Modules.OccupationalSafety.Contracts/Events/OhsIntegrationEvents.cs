using Sense.Shared.Contracts.Events;

namespace Sense.Modules.OccupationalSafety.Contracts.Events;

/// <summary>
/// A work accident was recorded. Documents turns it into the employee's "İş kazası tutanağı" requirement, due on the
/// SGK notification deadline (the report is the basis of the SGK notification).
/// </summary>
public sealed record WorkAccidentRecorded(
    Guid TenantId, Guid IncidentId, Guid EmployeeId, DateOnly EventDate, DateOnly SgkNotificationDeadline)
    : IntegrationEvent(TenantId);

/// <summary>
/// Daily reminder (JobType.OhsSgkNotificationReminder): work accidents not yet notified to SGK whose deadline is today
/// or tomorrow, and those already past it. Counts only; the list itself is GET /api/v1/ohs/incidents.
/// </summary>
public sealed record SgkNotificationReminderDue(Guid TenantId, int DueSoonCount, int OverdueCount)
    : IntegrationEvent(TenantId);
