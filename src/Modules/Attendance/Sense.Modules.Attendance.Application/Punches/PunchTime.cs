using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutma saat dönüşümleri (Z0.1): <see cref="Punch.OccurredAtUtc"/> UTC saklanır, <see cref="AttendanceDay"/> ise kiracının yerel
/// duvar saatiyle çalışır. Dilim verilmezse yerel = UTC.
/// </summary>
public static class PunchTime
{
    private const int MinutesPerHour = 60;

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo? zone)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateTime.SpecifyKind(zone is null ? asUtc : TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone), DateTimeKind.Unspecified);
    }

    /// <summary>Yerel duvar saatini UTC'ye çevirir; yaz saati boşluğundaki (geçersiz) saatler dilimin o anki ofsetiyle kaydırılır, hata atılmaz.</summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo? zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone?.GetUtcOffset(unspecified) ?? TimeSpan.Zero;
        return DateTime.SpecifyKind(unspecified - offset, DateTimeKind.Utc);
    }

    /// <summary>Sabit ofsetli dilim (kiracı dilimi bilinmediğinde "şimdi"nin yerel−UTC farkından türetilir).</summary>
    public static TimeZoneInfo FixedOffset(TimeSpan offset)
    {
        var rounded = TimeSpan.FromMinutes(Math.Round(offset.TotalMinutes));
        if (rounded == TimeSpan.Zero)
        {
            return TimeZoneInfo.Utc;
        }

        var id = $"UTC{(rounded < TimeSpan.Zero ? "-" : "+")}{Math.Abs(rounded.Hours):00}:{Math.Abs(rounded.Minutes % MinutesPerHour):00}";
        return TimeZoneInfo.CreateCustomTimeZone(id, rounded, id, id);
    }

    public static AttendanceDaySource ToDaySource(PunchSource source) => source switch
    {
        PunchSource.Manual => AttendanceDaySource.Manual,
        PunchSource.Import => AttendanceDaySource.Import,
        PunchSource.Device => AttendanceDaySource.Device,
        PunchSource.SelfService => AttendanceDaySource.SelfService,
        PunchSource.Request => AttendanceDaySource.Request,
        PunchSource.Terminal => AttendanceDaySource.Terminal,
        PunchSource.DeviceIntegration => AttendanceDaySource.DeviceIntegration,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static PunchSource ToPunchSource(AttendanceDaySource source) => source switch
    {
        AttendanceDaySource.Manual => PunchSource.Manual,
        AttendanceDaySource.Import => PunchSource.Import,
        AttendanceDaySource.Device => PunchSource.Device,
        AttendanceDaySource.SelfService => PunchSource.SelfService,
        AttendanceDaySource.Request => PunchSource.Request,
        AttendanceDaySource.Terminal => PunchSource.Terminal,
        AttendanceDaySource.DeviceIntegration => PunchSource.DeviceIntegration,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
