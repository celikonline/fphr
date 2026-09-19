using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>
/// The one "giriş kaydı oluştur" upsert every check-in channel shares (S-31 kabul kriteri 4: manuel kayıt, PDKS
/// Excel/cihaz içe alma ve self-servis QR/GPS check-in hepsi buradan geçer, aynı upsert mantığını tekrar etmez):
/// resolve the planned day from the work schedule, then either overwrite the existing <see cref="AttendanceDay"/>
/// for (employee, date) or create a new one - same "aynı çalışan+gün tekrar yüklenirse üzerine yazar" rule
/// everywhere it's used from.
/// </summary>
internal static class AttendanceDayUpsert
{
    public static async Task<Result<AttendanceDay>> UpsertAsync(
        IAttendanceDayRepository days, IWorkScheduleReadService schedules, Guid tenantId, Guid employeeId, DateOnly date,
        TimeOnly? checkIn, TimeOnly? checkOut, AttendanceDaySource source, bool overnight, CancellationToken cancellationToken,
        int actualBreakMinutes = 0, ShiftLimits? shiftLimits = null)
    {
        var (start, end, minutes, breakMinutes) = await PlannedDayResolver.ResolveAsync(schedules, employeeId, date, cancellationToken).ConfigureAwait(false);
        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var updated = existing.UpdateRecord(checkIn, checkOut, start, end, minutes, breakMinutes, source, overnight, actualBreakMinutes, shiftLimits);
            return updated.IsFailure ? updated.Error : existing;
        }

        var day = AttendanceDay.Record(tenantId, employeeId, date, checkIn, checkOut, start, end, minutes, breakMinutes, source, overnight, actualBreakMinutes, shiftLimits);
        days.Add(day);
        return day;
    }

    /// <summary>Maps the tenant's shift settings (or none) to the <see cref="ShiftLimits"/> the day's m.63/m.69 anomaly flags are computed against.</summary>
    public static ShiftLimits ToShiftLimits(ShiftSettings? settings) =>
        settings is null
            ? ShiftLimits.None
            : new ShiftLimits(settings.MaxDailyMinutes, settings.NightWorkEnabled, settings.NightWorkStart, settings.NightWorkEnd, settings.NightWorkMaxMinutes);
}
