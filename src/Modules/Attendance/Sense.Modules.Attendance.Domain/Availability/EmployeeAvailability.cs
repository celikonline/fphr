using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Availability;

public enum AvailabilityKind
{
    /// <summary>"Bugün çalışmak istemiyorum".</summary>
    Unavailable = 0,

    /// <summary>"Haftalık iznimi kullanmak istiyorum".</summary>
    WeeklyRestRequest = 1,
}

public enum AvailabilityStatus
{
    Active = 0,
    Cancelled = 1,
}

/// <summary>
/// "Müsait değilim" self-service entry (11.3.6): a day (or part of a day) the employee reports as unavailable so the
/// planner sees it before assigning a shift (<see cref="Shifts.ShiftAssignment"/>). Start/End null means the whole
/// day. Consumed by AssignShiftHandler/RepeatShiftAssignmentsHandler as a conflict check (package A5); LeaveApproved
/// blocking a day automatically is deferred (needs a Leave.Contracts read port that does not exist yet).
/// </summary>
public sealed class EmployeeAvailability : TenantAggregateRoot<Guid>
{
    private EmployeeAvailability()
    {
    }

    private EmployeeAvailability(Guid id, Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? start, TimeOnly? end, AvailabilityKind kind, string? note)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Date = date;
        Start = start;
        End = end;
        Kind = kind;
        Note = note;
        Status = AvailabilityStatus.Active;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Null on both means the whole day.</summary>
    public TimeOnly? Start { get; private set; }

    public TimeOnly? End { get; private set; }

    public AvailabilityKind Kind { get; private set; }

    public string? Note { get; private set; }

    public AvailabilityStatus Status { get; private set; }

    public static Result<EmployeeAvailability> Create(Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? start, TimeOnly? end, AvailabilityKind kind, string? note)
    {
        if (start is not null != end is not null || (start is not null && end is not null && start >= end))
        {
            return Error.Validation(AttendanceDomainErrors.AvailabilityTimeRangeInvalid);
        }

        return new EmployeeAvailability(Guid.CreateVersion7(), tenantId, employeeId, date, start, end, kind, note);
    }

    /// <summary>Whole-day block or a partial window overlapping <paramref name="rangeStart"/>/<paramref name="rangeEnd"/>.</summary>
    public bool ConflictsWith(TimeOnly rangeStart, TimeOnly rangeEnd)
    {
        if (Status != AvailabilityStatus.Active)
        {
            return false;
        }

        if (Start is null || End is null)
        {
            return true;
        }

        return Start.Value < rangeEnd && rangeStart < End.Value;
    }

    public Result Cancel()
    {
        if (Status == AvailabilityStatus.Cancelled)
        {
            return Error.Conflict(AttendanceDomainErrors.AvailabilityAlreadyCancelled);
        }

        Status = AvailabilityStatus.Cancelled;
        return Result.Success();
    }
}
