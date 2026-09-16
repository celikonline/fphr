using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.AttendanceDays;

/// <summary>
/// One employee's one calendar day of attendance (11.3.6 "Puantaj hücresi"): plan vs. actual, computed against the
/// <see cref="WorkSchedules.WorkSchedule"/> day in effect for that date. PlannedStart/End are a snapshot taken at
/// record time (mirrors <see cref="WorkSchedules.EmployeeWorkScheduleAssignment"/>'s dated-history approach) so a
/// later schedule edit never rewrites already-recorded history. One row per (EmployeeId, Date); a re-import or
/// re-record overwrites it (11.3.6: "aynı çalışan+gün tekrar yüklenirse üzerine yazar").
/// </summary>
public sealed class AttendanceDay : TenantAggregateRoot<Guid>
{
    private AttendanceDay()
    {
    }

    private AttendanceDay(
        Guid id, Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut,
        TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, AttendanceDaySource source)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        Date = date;
        Source = source;
        ResolutionState = AttendanceResolutionState.Unresolved;
        Recompute(checkIn, checkOut, plannedStart, plannedEnd, plannedMinutes, breakMinutes);
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly Date { get; private set; }

    public TimeOnly? CheckIn { get; private set; }

    public TimeOnly? CheckOut { get; private set; }

    /// <summary>Schedule snapshot at record time; null on both when the date was not a working day.</summary>
    public TimeOnly? PlannedStart { get; private set; }

    public TimeOnly? PlannedEnd { get; private set; }

    public int PlannedMinutes { get; private set; }

    public int BreakMinutes { get; private set; }

    public int WorkedMinutes { get; private set; }

    public AttendanceAnomaly Anomalies { get; private set; }

    public AttendanceResolutionState ResolutionState { get; private set; }

    /// <summary>"Çalışılan saat" resolution (package A3): overrides <see cref="WorkedMinutes"/> for reporting without re-deriving from CheckIn/CheckOut.</summary>
    public int? OverrideWorkedMinutes { get; private set; }

    public string? OverrideReason { get; private set; }

    public AttendanceDaySource Source { get; private set; }

    public static AttendanceDay Record(
        Guid tenantId, Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut,
        TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, AttendanceDaySource source) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, date, checkIn, checkOut, plannedStart, plannedEnd, plannedMinutes, breakMinutes, source);

    /// <summary>Overwrites this day's check-in/out and re-derives worked minutes and anomaly flags (re-import or re-record).</summary>
    public Result UpdateRecord(TimeOnly? checkIn, TimeOnly? checkOut, TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes, AttendanceDaySource source)
    {
        if (checkIn is not null && checkOut is not null && checkOut <= checkIn)
        {
            return Error.Validation(AttendanceDomainErrors.DayCheckOutBeforeCheckIn);
        }

        Source = source;
        Recompute(checkIn, checkOut, plannedStart, plannedEnd, plannedMinutes, breakMinutes);
        return Result.Success();
    }

    /// <summary>
    /// "Çalışılan saat" resolution (11.3.6 Az çalışma → Çalışılan saat): overrides <see cref="WorkedMinutes"/> for
    /// reporting and resolves the day without touching CheckIn/CheckOut. Also used as the manual-correction action
    /// for missing/unplanned-data anomalies that have no Overwork/Underwork flag.
    /// </summary>
    public Result ResolveWithOverride(int overrideWorkedMinutes, string? reason)
    {
        var guard = EnsureResolvable();
        if (guard.IsFailure)
        {
            return guard;
        }

        if (overrideWorkedMinutes < 0)
        {
            return Error.Validation(AttendanceDomainErrors.OverrideMinutesInvalid);
        }

        OverrideWorkedMinutes = overrideWorkedMinutes;
        OverrideReason = reason;
        ResolutionState = AttendanceResolutionState.Resolved;
        return Result.Success();
    }

    /// <summary>AddOvertime/CreateLeaveRequest resolution: the anomaly is addressed by a Leave-module record the caller links via <see cref="AnomalyResolution"/>, not by changing this day's own numbers.</summary>
    public Result MarkResolvedByLinkedEntity()
    {
        var guard = EnsureResolvable();
        if (guard.IsFailure)
        {
            return guard;
        }

        ResolutionState = AttendanceResolutionState.Resolved;
        return Result.Success();
    }

    /// <summary>Dismisses a flagged day without an override or a linked Leave record (manual "no action needed" call).</summary>
    public Result Ignore()
    {
        var guard = EnsureResolvable();
        if (guard.IsFailure)
        {
            return guard;
        }

        ResolutionState = AttendanceResolutionState.Ignored;
        return Result.Success();
    }

    private Result EnsureResolvable()
    {
        if (Anomalies == AttendanceAnomaly.None)
        {
            return Error.Validation(AttendanceDomainErrors.NoAnomalyToResolve);
        }

        if (ResolutionState != AttendanceResolutionState.Unresolved)
        {
            return Error.Validation(AttendanceDomainErrors.DayAlreadyResolved);
        }

        return Result.Success();
    }

    private void Recompute(TimeOnly? checkIn, TimeOnly? checkOut, TimeOnly? plannedStart, TimeOnly? plannedEnd, int plannedMinutes, int breakMinutes)
    {
        CheckIn = checkIn;
        CheckOut = checkOut;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        PlannedMinutes = plannedMinutes;
        BreakMinutes = breakMinutes;

        // A re-record/re-import overwrites the day's own facts (class doc: "aynı çalışan+gün tekrar yüklenirse üzerine
        // yazar"), so any prior manual resolution is superseded along with it - new data means a fresh evaluation.
        OverrideWorkedMinutes = null;
        OverrideReason = null;

        WorkedMinutes = checkIn is not null && checkOut is not null
            ? Math.Max((int)(checkOut.Value.ToTimeSpan() - checkIn.Value.ToTimeSpan()).TotalMinutes - breakMinutes, 0)
            : 0;

        var isWorkingDay = plannedStart is not null && plannedEnd is not null;
        var anomalies = AttendanceAnomaly.None;

        if (isWorkingDay)
        {
            if (checkIn is null && checkOut is null)
            {
                anomalies |= AttendanceAnomaly.NoData;
            }

            if (checkIn is null && checkOut is not null)
            {
                anomalies |= AttendanceAnomaly.MissingCheckIn;
            }

            if (checkOut is null && checkIn is not null)
            {
                anomalies |= AttendanceAnomaly.MissingCheckOut;
            }

            if (checkIn > plannedStart)
            {
                anomalies |= AttendanceAnomaly.LateArrival;
            }

            if (checkOut is not null && checkOut < plannedEnd)
            {
                anomalies |= AttendanceAnomaly.EarlyLeave;
            }

            if (checkOut > plannedEnd)
            {
                anomalies |= AttendanceAnomaly.LateLeave;
            }

            if (checkIn is not null && checkOut is not null)
            {
                if (WorkedMinutes > plannedMinutes)
                {
                    anomalies |= AttendanceAnomaly.Overwork;
                }
                else if (WorkedMinutes < plannedMinutes)
                {
                    anomalies |= AttendanceAnomaly.Underwork;
                }
            }
        }
        else if (checkIn is not null || checkOut is not null)
        {
            anomalies |= AttendanceAnomaly.UnplannedWork;
        }

        Anomalies = anomalies;
        ResolutionState = anomalies == AttendanceAnomaly.None ? AttendanceResolutionState.Resolved : AttendanceResolutionState.Unresolved;
    }
}
