using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutma öncesi bir <see cref="AttendanceDay"/>'in giriş/çıkışından okutma üretir - B1 geri doldurma işi (<c>PunchBackfillJob</c>) ve
/// taşınmamış bir güne yeni okutma geldiğinde (<see cref="PunchDayWriter.LoadDayAsync"/>) aynı kural. Kural, yeniden türetilen günün
/// öncekiyle birebir aynı çıkması içindir: giriş = In, çıkış = Out (çıkış girişten önce/eşitse ertesi gün, gece vardiyası); PDKS'de ölçülmüş
/// bir gerçek mola (<see cref="AttendanceDay.ActualBreakMinutes"/>) varsa giriş-çıkış aralığının ortasına o uzunlukta temsili bir Out→In çifti
/// konur (gerçek ara saatler saklanmadığı için; gerçek ara okutmalar gelince <see cref="DayFromPunches"/> bu çifti yok sayar). Kaynak günün
/// kaynağıdır, gerekçe <see cref="PunchDayWriter.LegacyReason"/>. Her okutma işaretlidir: <c>SourceRef</c> = <see cref="MarkerPrefix"/> +
/// gün id (mola çifti için <see cref="BreakMarkerPrefix"/> + gün id) - idempotans ve geri alma bu işaretle yapılır.
/// </summary>
public static class LegacyDayPunches
{
    public const string MarkerPrefix = "backfill:";
    public const string BreakMarkerPrefix = "backfill-break:";

    /// <param name="selfCheckInAt">Self-servis günlerde aynı anın denetim satırı (konum/koordinat okutmaya kopyalanır); yoksa null.</param>
    public static IReadOnlyList<Punch> From(AttendanceDay day, TimeZoneInfo zone, Func<DateTime, PunchDirection, SelfCheckIn?>? selfCheckInAt = null)
    {
        var source = PunchTime.ToPunchSource(day.Source);
        var isManual = day.Source == AttendanceDaySource.Manual;
        var dayMarker = MarkerPrefix + day.Id;
        var breakMarker = BreakMarkerPrefix + day.Id;
        DateTime? checkIn = day.CheckIn is { } i ? day.Date.ToDateTime(i) : null;
        DateTime? checkOut = day.CheckOut is { } o ? day.Date.ToDateTime(o) : null;
        if (checkIn is { } start && checkOut is { } end && end <= start)
        {
            checkOut = end.AddDays(1);
        }

        var local = new List<(DateTime At, PunchDirection Direction, string Marker)>();
        if (checkIn is { } inAt)
        {
            local.Add((inAt, PunchDirection.In, dayMarker));
        }

        if (checkIn is { } from && checkOut is { } to && day.ActualBreakMinutes > 0 && (to - from).TotalMinutes > day.ActualBreakMinutes)
        {
            var breakSpan = TimeSpan.FromMinutes(day.ActualBreakMinutes);
            var breakOut = from + ((to - from - breakSpan) / 2);
            local.Add((breakOut, PunchDirection.Out, breakMarker));
            local.Add((breakOut + breakSpan, PunchDirection.In, breakMarker));
        }

        if (checkOut is { } outAt)
        {
            local.Add((outAt, PunchDirection.Out, dayMarker));
        }

        return local
            .Select(p =>
            {
                var utc = Punch.NormalizeOccurredAt(PunchTime.ToUtc(p.At, zone));
                var audit = source == PunchSource.SelfService ? selfCheckInAt?.Invoke(utc, p.Direction) : null;
                return Punch.Create(
                    day.TenantId, day.EmployeeId, utc, day.Date, p.Direction, source, p.Marker, null, audit?.LocationId, audit?.Latitude, audit?.Longitude,
                    isManual, null, PunchDayWriter.LegacyReason, utc);
            })
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();
    }
}
