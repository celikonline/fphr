using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>
/// The one "giriş kaydı oluştur" path every check-in channel shares (S-31 kabul kriteri 4: manuel kayıt, PDKS Excel/cihaz içe alma ve
/// self-servis QR/GPS check-in hepsi aynı mantıktan geçer). Zaman-devam Z0.1 ile bu yol okutma tabanlıdır: kanal önce ham okutmayı
/// (<see cref="Domain.Punches.Punch"/>) yazar, gün okutmalardan türetilir - bkz. <see cref="Punches.PunchDayWriter"/> (gün düzeyi kanallar
/// için <c>ReplaceDayAsync</c>: "aynı çalışan+gün tekrar yüklenirse üzerine yazar"; okutma kanalları için <c>Add</c> + <c>RebuildAsync</c>).
/// </summary>
internal static class AttendanceDayUpsert
{
    /// <summary>Maps the tenant's shift settings (or none) to the <see cref="ShiftLimits"/> the day's m.63/m.69 anomaly flags are computed against.</summary>
    public static ShiftLimits ToShiftLimits(ShiftSettings? settings) =>
        settings is null
            ? ShiftLimits.None
            : new ShiftLimits(settings.MaxDailyMinutes, settings.NightWorkEnabled, settings.NightWorkStart, settings.NightWorkEnd, settings.NightWorkMaxMinutes);
}
