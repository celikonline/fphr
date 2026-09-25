using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutma öncesi bir <see cref="AttendanceDay"/>'in giriş/çıkışından okutma üretir (B1 veri taşıması - SQL eşi
/// <c>AttendanceBackfillPunches</c> migration'ı - ve taşınmamış bir güne yeni okutma geldiğinde). Kural, yeniden türetilen günün taşıma
/// öncesiyle birebir aynı çıkması içindir: giriş = In, çıkış = Out (çıkış girişten önce/eşitse ertesi gün, gece vardiyası); PDKS'de ölçülmüş
/// bir gerçek mola (<see cref="AttendanceDay.ActualBreakMinutes"/>) varsa, giriş-çıkış aralığının ortasına o uzunlukta bir Out→In çifti konur
/// (gerçek ara saatler saklanmadığı için temsili). Kaynak günün kaynağıdır; gerekçe <see cref="PunchDayWriter.LegacyReason"/>.
/// </summary>
public static class LegacyDayPunches
{
    public static IReadOnlyList<Punch> From(AttendanceDay day, TimeZoneInfo zone)
    {
        var source = PunchTime.ToPunchSource(day.Source);
        var isManual = day.Source == AttendanceDaySource.Manual;
        DateTime? checkIn = day.CheckIn is { } i ? day.Date.ToDateTime(i) : null;
        DateTime? checkOut = day.CheckOut is { } o ? day.Date.ToDateTime(o) : null;
        if (checkIn is { } start && checkOut is { } end && end <= start)
        {
            checkOut = end.AddDays(1);
        }

        var local = new List<(DateTime At, PunchDirection Direction)>();
        if (checkIn is { } inAt)
        {
            local.Add((inAt, PunchDirection.In));
        }

        if (checkIn is { } from && checkOut is { } to && day.ActualBreakMinutes > 0 && (to - from).TotalMinutes > day.ActualBreakMinutes)
        {
            var breakSpan = TimeSpan.FromMinutes(day.ActualBreakMinutes);
            var breakOut = from + ((to - from - breakSpan) / 2);
            local.Add((breakOut, PunchDirection.Out));
            local.Add((breakOut + breakSpan, PunchDirection.In));
        }

        if (checkOut is { } outAt)
        {
            local.Add((outAt, PunchDirection.Out));
        }

        return local
            .Select(p =>
            {
                var utc = PunchTime.ToUtc(p.At, zone);
                return Punch.Create(day.TenantId, day.EmployeeId, utc, day.Date, p.Direction, source, null, null, null, null, null, isManual, null, PunchDayWriter.LegacyReason, utc);
            })
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();
    }
}
