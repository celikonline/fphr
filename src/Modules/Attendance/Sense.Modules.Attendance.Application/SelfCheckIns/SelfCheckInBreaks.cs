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
/// day's check-in (latest break row is a <see cref="PunchDirection.BreakStart"/>); no extra column is stored. Ending a
/// break (or checking out during one) adds its whole minutes to <see cref="AttendanceDay.ActualBreakMinutes"/> through
/// <see cref="AttendanceDayUpsert"/>, which re-runs <see cref="DailyWorkCalculator"/>; check-in/out are never changed.
/// </summary>
internal static class SelfCheckInBreaks
{
    public static async Task<Result<SelfCheckInPunch.Applied>> ApplyAsync(
        Guid tenantId, Guid employeeId, PunchDirection direction, TenantLocalNow now, IAttendanceDayRepository days,
        IWorkScheduleReadService schedules, IShiftSettingsRepository shiftSettings, ISelfCheckInRepository selfCheckIns, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.Local);
        var time = TimeOnly.FromDateTime(now.Local);
        var day = await FindOpenDayAsync(days, employeeId, today, cancellationToken);
        if (day is null)
        {
            return Error.Validation(AttendanceErrors.SelfCheckInBreakNotCheckedIn);
        }

        var openBreakUtc = await FindOpenBreakStartUtcAsync(selfCheckIns, employeeId, day, now, cancellationToken);
        if (direction == PunchDirection.BreakStart)
        {
            return openBreakUtc is null
                ? new SelfCheckInPunch.Applied(day.Id, day.Date, time)
                : Error.Validation(AttendanceErrors.SelfCheckInBreakAlreadyOpen);
        }

        if (openBreakUtc is not { } startedUtc)
        {
            return Error.Validation(AttendanceErrors.SelfCheckInBreakNotOpen);
        }

        var limits = AttendanceDayUpsert.ToShiftLimits(await shiftSettings.FindAsync(cancellationToken));
        var upserted = await AttendanceDayUpsert.UpsertAsync(
            days, schedules, tenantId, employeeId, day.Date, day.CheckIn, day.CheckOut, AttendanceDaySource.SelfService, overnight: false,
            cancellationToken, day.ActualBreakMinutes + ElapsedMinutes(startedUtc, now.Utc), limits);
        return upserted.IsFailure ? upserted.Error : new SelfCheckInPunch.Applied(upserted.Value.Id, day.Date, time);
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

        var checkedInUtc = DateTime.SpecifyKind(day.Date.ToDateTime(checkIn) - UtcOffset(now), DateTimeKind.Utc);
        var latest = await selfCheckIns.FindLatestBreakPunchAsync(employeeId, checkedInUtc, cancellationToken);
        return latest is { Direction: PunchDirection.BreakStart } ? latest.TimestampUtc : null;
    }

    /// <summary>Whole minutes between two instants (partial minute dropped, never negative).</summary>
    public static int ElapsedMinutes(DateTime fromUtc, DateTime toUtc) => Math.Max((int)(toUtc - fromUtc).TotalMinutes, 0);

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

    private static TimeSpan UtcOffset(TenantLocalNow now) => now.Local - now.Utc;
}
