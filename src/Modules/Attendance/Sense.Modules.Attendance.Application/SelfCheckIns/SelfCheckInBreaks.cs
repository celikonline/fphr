using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>
/// Self-servis mola başı/sonu (mobil yeniden tasarım §6). A break belongs to the caller's open day - today's record
/// checked in and not yet checked out, or (night shift) yesterday's unmatched evening check-in, the same day the status
/// endpoint reports as "inside". The open break is derived from the <see cref="SelfCheckIn"/> audit rows since that
/// day's check-in (latest break row is a <see cref="PunchDirection.BreakStart"/>); no extra column is stored. Each break
/// start/end is also recorded as a <see cref="PunchDirection.BreakStart"/>/<see cref="PunchDirection.BreakEnd"/> punch on that
/// day (zaman-devam Z0.1), and the day is re-derived from its punches: <see cref="Punches.DayFromPunches"/> adds each closed
/// break (ended by a break end or a check-out) to <see cref="AttendanceDay.ActualBreakMinutes"/> - so a recalculation keeps it -
/// and never changes check-in/out.
/// </summary>
internal static class SelfCheckInBreaks
{
    public static async Task<Result<SelfCheckInPunch.Applied>> ApplyAsync(
        SelfCheckIn audit, TenantLocalNow now, SelfCheckInPunch.Ports ports, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.Local);
        var day = await FindOpenDayAsync(ports.Days, audit.EmployeeId, today, cancellationToken);
        if (day is null)
        {
            return Error.Validation(AttendanceErrors.SelfCheckInBreakNotCheckedIn);
        }

        var openBreakUtc = await FindOpenBreakStartUtcAsync(ports.SelfCheckIns, audit.EmployeeId, day, now, cancellationToken);
        if (audit.Direction == PunchDirection.BreakStart && openBreakUtc is not null)
        {
            return Error.Validation(AttendanceErrors.SelfCheckInBreakAlreadyOpen);
        }

        if (audit.Direction == PunchDirection.BreakEnd && openBreakUtc is null)
        {
            return Error.Validation(AttendanceErrors.SelfCheckInBreakNotOpen);
        }

        return await SelfCheckInPunch.WriteAsync(audit, day.Date, now, ports, cancellationToken);
    }

    /// <summary>Today's record when checked in (open or closed); otherwise yesterday's still-open night shift; null when neither.</summary>
    public static async Task<AttendanceDay?> FindCurrentDayAsync(
        IAttendanceDayRepository days, Guid employeeId, DateOnly today, CancellationToken cancellationToken)
    {
        var record = await days.FindByEmployeeAndDateAsync(employeeId, today, cancellationToken);
        return record?.CheckIn is null
            ? await SelfCheckInPunch.FindOpenOvernightDayAsync(days, employeeId, today, cancellationToken) ?? record
            : record;
    }

    /// <summary>
    /// UTC start of the day's open break, or null. Only an open day (checked in, no check-out) can have one: a check-out
    /// closes any break still running, so the break rows are read only while the day is open.
    /// </summary>
    public static async Task<DateTime?> FindOpenBreakStartUtcAsync(
        ISelfCheckInRepository selfCheckIns, Guid employeeId, AttendanceDay day, TenantLocalNow now, CancellationToken cancellationToken)
    {
        if (day is not { CheckIn: { } checkIn, CheckOut: null })
        {
            return null;
        }

        var latest = await selfCheckIns.FindLatestBreakPunchAsync(employeeId, CheckedInUtc(day.Date, checkIn, now), cancellationToken);
        return latest is { Direction: PunchDirection.BreakStart } ? latest.TimestampUtc : null;
    }

    /// <summary>
    /// The day's breaks, oldest first (status tiles survive an app restart): a break start opens a break, the next break
    /// end or check-out closes it. A break still open on a day that was checked out through another channel ends at the
    /// day's check-out; one still open on an open day has no end. Empty when the day has no check-in.
    /// </summary>
    public static async Task<IReadOnlyList<MyBreakDto>> ListDayBreaksAsync(
        ISelfCheckInRepository selfCheckIns, Guid employeeId, AttendanceDay day, TenantLocalNow now, CancellationToken cancellationToken)
    {
        if (day.CheckIn is not { } checkIn)
        {
            return [];
        }

        var punches = await selfCheckIns.ListBreakAndOutPunchesAsync(employeeId, CheckedInUtc(day.Date, checkIn, now), cancellationToken);
        var breaks = new List<MyBreakDto>();
        TimeOnly? openStart = null;
        foreach (var punch in punches)
        {
            var time = ToLocalTime(punch.TimestampUtc, now);
            if (punch.Direction == PunchDirection.BreakStart)
            {
                openStart ??= time;
            }
            else if (openStart is { } start)
            {
                breaks.Add(new MyBreakDto(start, time));
                openStart = null;
            }
        }

        if (openStart is { } stillOpen)
        {
            breaks.Add(new MyBreakDto(stillOpen, day.CheckOut));
        }

        return breaks;
    }

    /// <summary>Tenant wall-clock time of a UTC instant, to the second (the status contract is "HH:mm:ss").</summary>
    public static TimeOnly ToLocalTime(DateTime utc, TenantLocalNow now)
    {
        var local = TimeOnly.FromDateTime(utc + UtcOffset(now));
        return new TimeOnly(local.Hour, local.Minute, local.Second);
    }

    private static async Task<AttendanceDay?> FindOpenDayAsync(
        IAttendanceDayRepository days, Guid employeeId, DateOnly today, CancellationToken cancellationToken)
    {
        var day = await FindCurrentDayAsync(days, employeeId, today, cancellationToken);
        return day is { CheckIn: not null, CheckOut: null } ? day : null;
    }

    private static DateTime CheckedInUtc(DateOnly date, TimeOnly checkIn, TenantLocalNow now) =>
        DateTime.SpecifyKind(date.ToDateTime(checkIn) - UtcOffset(now), DateTimeKind.Utc);

    private static TimeSpan UtcOffset(TenantLocalNow now) => now.Local - now.Utc;
}
