using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.WorkSchedules;

/// <summary>
/// One day of a <see cref="WorkSchedule"/> (11.3.6: "Salı 15:30 bitiş"; "Perşembe tatil, Cumartesi çalışma"). Mutable
/// child value (EF-owned, stored as JSON on the schedule row) so <see cref="WorkSchedule.SetDays"/> can replace the
/// whole week atomically; validated together in <see cref="WorkSchedule.ValidateDays"/> (7 distinct days required).
/// </summary>
public sealed class WorkScheduleDay
{
    public WorkScheduleDay()
    {
    }

    private WorkScheduleDay(DayOfWeek dayOfWeek, bool isWorkingDay, TimeOnly? start, TimeOnly? end, TimeOnly? breakStart, TimeOnly? breakEnd)
    {
        DayOfWeek = dayOfWeek;
        IsWorkingDay = isWorkingDay;
        Start = start;
        End = end;
        BreakStart = breakStart;
        BreakEnd = breakEnd;
    }

    public DayOfWeek DayOfWeek { get; private set; }

    public bool IsWorkingDay { get; private set; }

    public TimeOnly? Start { get; private set; }

    public TimeOnly? End { get; private set; }

    public TimeOnly? BreakStart { get; private set; }

    public TimeOnly? BreakEnd { get; private set; }

    public static WorkScheduleDay Working(DayOfWeek dayOfWeek, TimeOnly start, TimeOnly end, TimeOnly? breakStart = null, TimeOnly? breakEnd = null) =>
        new(dayOfWeek, true, start, end, breakStart, breakEnd);

    public static WorkScheduleDay Off(DayOfWeek dayOfWeek) => new(dayOfWeek, false, null, null, null, null);

    /// <summary>Minutes actually worked (span minus break), used to derive <see cref="WorkSchedule.WeeklyMinutes"/>.</summary>
    public int WorkingMinutes()
    {
        if (!IsWorkingDay || Start is null || End is null)
        {
            return 0;
        }

        var minutes = (int)(End.Value.ToTimeSpan() - Start.Value.ToTimeSpan()).TotalMinutes;
        if (BreakStart is not null && BreakEnd is not null)
        {
            minutes -= (int)(BreakEnd.Value.ToTimeSpan() - BreakStart.Value.ToTimeSpan()).TotalMinutes;
        }

        return Math.Max(minutes, 0);
    }

    internal Result Validate()
    {
        if (!IsWorkingDay)
        {
            return Result.Success();
        }

        if (Start is null || End is null || End <= Start)
        {
            return Error.Validation(AttendanceDomainErrors.ScheduleDayRangeInvalid);
        }

        if (BreakStart is null != BreakEnd is null)
        {
            return Error.Validation(AttendanceDomainErrors.ScheduleDayBreakInvalid);
        }

        if (BreakStart is not null && (BreakStart < Start || BreakEnd > End || BreakEnd <= BreakStart))
        {
            return Error.Validation(AttendanceDomainErrors.ScheduleDayBreakInvalid);
        }

        return Result.Success();
    }
}
