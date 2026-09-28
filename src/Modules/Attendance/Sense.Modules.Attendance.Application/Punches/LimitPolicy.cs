using Sense.Modules.Attendance.Domain.AttendanceDays;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Günün hangi kiracı limitleriyle (m.63 günlük üst sınır, m.69 gece penceresi/üst sınırı) kurulduğu - TEK YER (I0 düzeltme turu 1).
/// Elle kayıt ve PDKS Excel günleri bugünkü gibi limitsiz kurulur (gece dakikası 0, limit bayrağı yok); cihaz, self-servis, talep ve
/// terminal günleri kiracı limitleriyle. <see cref="PunchDayWriter"/> her kanalda ve yeniden hesap (<c>PunchDayRebuilder</c>) günün
/// kaynağına göre bunu uygular, böylece yeniden hesap kayıt anındaki sonucu değiştirmez.
/// </summary>
public static class LimitPolicy
{
    public static ShiftLimits? For(AttendanceDaySource source, ShiftLimits? tenantLimits) =>
        source is AttendanceDaySource.Manual or AttendanceDaySource.Import ? null : tenantLimits;
}
