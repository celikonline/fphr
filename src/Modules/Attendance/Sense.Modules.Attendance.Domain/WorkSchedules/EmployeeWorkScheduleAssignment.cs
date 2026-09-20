using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Attendance.Domain.WorkSchedules;

/// <summary>Where an assignment came from (11.3.6: tekil atama / toplu işlem / işe alım varsayılanı).</summary>
public enum WorkScheduleAssignmentSource
{
    Manual = 0,
    Bulk = 1,
    Hire = 2,
}

/// <summary>
/// Dated link between an employee and a <see cref="WorkSchedule"/> (11.3.6: "atama tarihinden itibaren geçerli").
/// Never mutated or deleted: assigning a new schedule adds a new row effective from that date, so past
/// <c>AttendanceDay</c> history (added in a later package) never needs recomputing. One row per (EmployeeId,
/// EffectiveFrom); the row with the latest EffectiveFrom on or before a date is the one in effect that day.
/// </summary>
public sealed class EmployeeWorkScheduleAssignment : TenantAggregateRoot<Guid>
{
    private EmployeeWorkScheduleAssignment()
    {
    }

    private EmployeeWorkScheduleAssignment(
        Guid id, Guid tenantId, Guid employeeId, Guid workScheduleId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        WorkScheduleAssignmentSource source, Guid? assignedByUserId)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        WorkScheduleId = workScheduleId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Source = source;
        AssignedByUserId = assignedByUserId;
    }

    public Guid EmployeeId { get; private set; }

    public Guid WorkScheduleId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Null means this is the employee's active work-schedule assignment.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public WorkScheduleAssignmentSource Source { get; private set; }

    public Guid? AssignedByUserId { get; private set; }

    public static EmployeeWorkScheduleAssignment Create(
        Guid tenantId, Guid employeeId, Guid workScheduleId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        WorkScheduleAssignmentSource source, Guid? assignedByUserId) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, workScheduleId, effectiveFrom, effectiveTo, source, assignedByUserId);

    public void UpdatePeriod(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }
}
