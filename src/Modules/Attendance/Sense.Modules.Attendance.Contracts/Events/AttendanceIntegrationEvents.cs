using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Attendance.Contracts.Events;

/// <summary>An employee's work schedule changed, effective from a date (11.3.6 "SenseİK tasarım kararı").</summary>
public sealed record WorkScheduleAssigned(Guid TenantId, Guid EmployeeId, Guid WorkScheduleId, DateOnly EffectiveFrom)
    : IntegrationEvent(TenantId);

/// <summary>
/// A calendar week (Monday-Sunday) for an employee now has every working day recorded with no unresolved anomaly
/// (11.3.6 "hiç uyuşmazlık kalmayınca haftalık puantaj tamamlanmış"). Published by the anomaly-resolution handlers
/// (package A3) whenever a resolve action happens to be the one that clears the last open day of its week; there is
/// no persisted "week" row, so this can fire again if the week is later reopened by a re-import and re-resolved.
/// </summary>
public sealed record TimesheetWeekCompleted(Guid TenantId, Guid EmployeeId, DateOnly WeekStart)
    : IntegrationEvent(TenantId);

/// <summary>
/// A batch of draft shift assignments for one org unit's one calendar week (Monday-Sunday) was published
/// (TALIMAT faz-06 tur "S-16"/"vardiya yayın olayı"). Raised by <c>PublishShiftAssignmentsHandler</c> once per
/// distinct (OrgUnit, WeekStart) group found among the published assignments' employees - a single publish call
/// spanning several units/weeks raises several events. No consumer yet (Faz 1 Notifications wires one up in a
/// later turn); employees with no org unit assigned are not represented by any event.
/// </summary>
public sealed record ShiftPlanPublished(Guid TenantId, Guid OrgUnitId, DateOnly WeekStart, IReadOnlyList<Guid> EmployeeIds)
    : IntegrationEvent(TenantId);
