using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutma → gün yazma yolu (Z0.1): her giriş kanalı (elle, Excel, PDKS cihazı, self-servis, <see cref="IPunchRecorder"/>) önce
/// <see cref="Punch"/> yazar, sonra günü okutmalardan (<see cref="DayFromPunches"/>) yeniden türetip <see cref="AttendanceDay"/>'e verir.
/// Tek bir komut/iş boyunca kullanılır: henüz kaydedilmemiş okutmaları da bilir (EF sorguları eklenmiş ama kaydedilmemiş satırları
/// döndürmez). Kaydetmez - çağıran kendi unit-of-work'ünü kaydeder.
/// </summary>
public sealed class PunchDayWriter(
    Guid tenantId, IAttendanceDayRepository days, IPunchRepository punches, IWorkScheduleReadService schedules, TimeZoneInfo zone, DateTime nowUtc)
{
    /// <summary>Taşıma (B1) ve eski günlerden türetilen okutmaların gerekçesi; geri alma (Down) bu değerle süzer.</summary>
    public const string LegacyReason = "Mevcut puantaj gününden taşındı";

    private readonly List<Punch> _pending = [];

    public TimeZoneInfo Zone => zone;

    /// <summary>
    /// Günün silinmemiş okutmaları (kaydedilmişler + bu yazıcıyla eklenenler), zamana göre. <paramref name="seedLegacy"/> açıksa ve günün
    /// hiç okutması yokken eski bir <see cref="AttendanceDay"/> giriş/çıkış taşıyorsa (taşıma öncesi kayıt) o değerlerden okutma üretilir -
    /// böylece yeni okutma eski günün bilgisini ezmez (G-6-22).
    /// </summary>
    public async Task<IReadOnlyList<Punch>> LoadDayAsync(Guid employeeId, DateOnly workDate, bool seedLegacy, CancellationToken cancellationToken)
    {
        var live = await LiveAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
        if (live.Count > 0 || !seedLegacy)
        {
            return live;
        }

        var legacy = await days.FindByEmployeeAndDateAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
        if (legacy is null || (legacy.CheckIn is null && legacy.CheckOut is null))
        {
            return live;
        }

        foreach (var punch in LegacyDayPunches.From(legacy, zone))
        {
            Track(punch);
        }

        return await LiveAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Yeni okutma ekler (henüz kaydetmez). <paramref name="validateAgainstUtc"/> ileri tarih kontrolünün "şimdi"sidir.</summary>
    public Result<Punch> Add(
        Guid employeeId, DateTime occurredAtUtc, DateOnly workDate, PunchDirection direction, PunchSource source, string? sourceRef, Guid? terminalId,
        Guid? locationId, double? latitude, double? longitude, bool isManual, Guid? createdByUserId, string? reason, DateTime validateAgainstUtc)
    {
        var created = Punch.Create(
            tenantId, employeeId, occurredAtUtc, workDate, direction, source, sourceRef, terminalId, locationId, latitude, longitude, isManual,
            createdByUserId, reason, validateAgainstUtc);
        if (created.IsSuccess)
        {
            Track(created.Value);
        }

        return created;
    }

    public Result SoftDelete(Punch punch, Guid? userId, string reason) => punch.SoftDelete(userId ?? Guid.Empty, reason, nowUtc);

    /// <summary>
    /// Gün düzeyinde kayıt (elle puantaj / Excel satırı: "aynı çalışan+gün tekrar yüklenirse üzerine yazar"): günün okutmaları istenen
    /// giriş/çıkışa eşitlenir - aynı olanlar korunur, diğerleri gerekçeyle yumuşak silinir, eksikler eklenir; sonra gün yeniden türetilir.
    /// Giriş/çıkış aynı takvim günündedir (bu kanallar gece vardiyası taşımaz).
    /// </summary>
    public async Task<Result<AttendanceDay>> ReplaceDayAsync(
        Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut, AttendanceDaySource source, Guid? actorUserId, string replaceReason,
        ShiftLimits? limits, CancellationToken cancellationToken)
    {
        var punchSource = PunchTime.ToPunchSource(source);
        var wanted = new List<(DateTime Utc, PunchDirection Direction)>();
        if (checkIn is { } inTime)
        {
            wanted.Add((PunchTime.ToUtc(date.ToDateTime(inTime), zone), PunchDirection.In));
        }

        if (checkOut is { } outTime)
        {
            wanted.Add((PunchTime.ToUtc(date.ToDateTime(outTime), zone), PunchDirection.Out));
        }

        foreach (var punch in await LiveAsync(employeeId, date, cancellationToken).ConfigureAwait(false))
        {
            var same = wanted.FindIndex(w => w.Utc == punch.OccurredAtUtc && w.Direction == punch.Direction && punch.Source == punchSource);
            if (same >= 0)
            {
                wanted.RemoveAt(same);
                continue;
            }

            var deleted = SoftDelete(punch, actorUserId, replaceReason);
            if (deleted.IsFailure)
            {
                return deleted.Error;
            }
        }

        foreach (var (utc, direction) in wanted)
        {
            // Gün düzeyi kanallar kendi tarih kuralını uygular (elle: gelecek gün yok; Excel: yok) - okutmanın saat bazlı ileri tarih
            // kontrolü bugünkü davranışı değiştirmesin diye okutmanın kendi anına göre yapılır.
            var added = Add(employeeId, utc, date, direction, punchSource, null, null, null, null, null, source == AttendanceDaySource.Manual, actorUserId, null, utc);
            if (added.IsFailure)
            {
                return added.Error;
            }
        }

        return await RebuildAsync(employeeId, date, source, limits, forceIncalculable: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Günü okutmalardan yeniden kurar ve <see cref="AttendanceDay"/>'i ekler/günceller. <paramref name="source"/> null ise en son okutmanın
    /// kaynağı (okutma yoksa günün mevcut kaynağı) kullanılır. <paramref name="forceIncalculable"/> gece vardiyası çakışması içindir.
    /// </summary>
    public async Task<Result<AttendanceDay>> RebuildAsync(
        Guid employeeId, DateOnly workDate, AttendanceDaySource? source, ShiftLimits? limits, bool forceIncalculable, CancellationToken cancellationToken)
    {
        var dayPunches = await LiveAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
        var (start, end, minutes, breakMinutes) = await PlannedDayResolver.ResolveAsync(schedules, employeeId, workDate, cancellationToken).ConfigureAwait(false);

        // PlannedDayResolver bugün "çizelge yok" ile "çalışma günü değil"i ayırt etmiyor: ikisi de hafta tatili (Rest) sayılır, böylece plansız
        // okutma bugünkü gibi UnplannedWork olur. "Hiç plan yok → Incalculable" kuralı (spec §5.1) çözümleyici bunu ayırt edince (B2) devreye girer.
        var plan = start is not null && end is not null ? new PlannedDay(start, end, breakMinutes) : PlannedDay.Rest;
        var result = DayFromPunches.Build(dayPunches, plan, zone);

        var existing = await days.FindByEmployeeAndDateAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
        var daySource = source
            ?? (dayPunches.Count > 0 ? PunchTime.ToDaySource(dayPunches[^1].Source) : existing?.Source ?? AttendanceDaySource.Manual);

        AttendanceDay day;
        if (existing is null)
        {
            day = AttendanceDay.Record(
                tenantId, employeeId, workDate, result.CheckIn, result.CheckOut, start, end, minutes, breakMinutes, daySource, result.Overnight,
                result.ActualBreakMinutes, limits);
            days.Add(day);
        }
        else
        {
            var updated = existing.UpdateRecord(
                result.CheckIn, result.CheckOut, start, end, minutes, breakMinutes, daySource, result.Overnight, result.ActualBreakMinutes, limits);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            day = existing;
        }

        if (result.Incalculable || (forceIncalculable && dayPunches.Count > 0))
        {
            day.MarkIncalculable();
        }

        return day;
    }

    private void Track(Punch punch)
    {
        punches.Add(punch);
        _pending.Add(punch);
    }

    private async Task<List<Punch>> LiveAsync(Guid employeeId, DateOnly workDate, CancellationToken cancellationToken)
    {
        var stored = await punches.ListForDayAsync(tenantId, employeeId, workDate, cancellationToken).ConfigureAwait(false);
        var storedIds = stored.Select(p => p.Id).ToHashSet();
        return stored
            .Concat(_pending.Where(p => p.EmployeeId == employeeId && p.WorkDate == workDate && !storedIds.Contains(p.Id)))
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.OccurredAtUtc)
            .ToList();
    }
}
