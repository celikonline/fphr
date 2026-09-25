using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>
/// The one "giriş kaydı oluştur" path every check-in channel shares (S-31 kabul kriteri 4: manuel kayıt, PDKS Excel/cihaz içe alma ve
/// self-servis QR/GPS check-in hepsi aynı mantıktan geçer). Zaman-devam Z0.1 ile bu yol okutma tabanlıdır: kanal önce ham okutmayı
/// (<see cref="Domain.Punches.Punch"/>) yazar, gün okutmalardan türetilir - bkz. <see cref="Punches.PunchDayWriter"/> (gün düzeyi kanallar
/// için <c>ReplaceDayAsync</c>: "aynı çalışan+gün tekrar yüklenirse üzerine yazar"; okutma kanalları için <c>Add</c> + <c>RebuildAsync</c>).
/// </summary>
internal static class AttendanceDayUpsert
{
    /// <summary>Z0.2: planned through <see cref="IPlannedDayResolver"/> (K2). Only the pre-punch recalculation rebuilder still uses it.</summary>
    public static async Task<Result<AttendanceDay>> UpsertAsync(
        IAttendanceDayRepository days, IPlannedDayResolver planner, Guid tenantId, Guid employeeId, DateOnly date,
        TimeOnly? checkIn, TimeOnly? checkOut, AttendanceDaySource source, bool overnight, CancellationToken cancellationToken,
        int actualBreakMinutes = 0, ShiftLimits? shiftLimits = null)
    {
        var plan = await planner.ResolveAsync(employeeId, date, checkIn, cancellationToken).ConfigureAwait(false) ?? PlannedDay.NonWorking();
        var limits = plan.Rules is null ? shiftLimits : (shiftLimits ?? ShiftLimits.None) with { Rules = plan.Rules };
        var existing = await days.FindByEmployeeAndDateAsync(employeeId, date, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var updated = existing.UpdateRecord(
                checkIn, checkOut, plan.Start, plan.End, plan.PlannedMinutes, plan.BreakMinutes, source, overnight, actualBreakMinutes, limits);
            return updated.IsFailure ? updated.Error : existing;
        }

        var day = AttendanceDay.Record(
            tenantId, employeeId, date, checkIn, checkOut, plan.Start, plan.End, plan.PlannedMinutes, plan.BreakMinutes, source, overnight,
            actualBreakMinutes, limits);
        days.Add(day);
        return day;
    }

    /// <summary>Maps the tenant's shift settings (or none) to the <see cref="ShiftLimits"/> the day's m.63/m.69 anomaly flags are computed against.</summary>
    public static ShiftLimits ToShiftLimits(ShiftSettings? settings) =>
        settings is null
            ? ShiftLimits.None
            : new ShiftLimits(settings.MaxDailyMinutes, settings.NightWorkEnabled, settings.NightWorkStart, settings.NightWorkEnd, settings.NightWorkMaxMinutes);
}
