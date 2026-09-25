using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutma → gün yazma yolu (Z0.1): her giriş kanalı (elle, Excel, PDKS cihazı, self-servis, <see cref="IPunchRecorder"/>) önce
/// <see cref="Punch"/> yazar, sonra günü okutmalardan (<see cref="DayFromPunches"/>) yeniden türetip <see cref="AttendanceDay"/>'e verir.
/// Tek bir komut/iş boyunca kullanılır: henüz kaydedilmemiş okutmaları ve yeni eklenen günleri de bilir (EF sorguları eklenmiş ama
/// kaydedilmemiş satırları döndürmez). K6 kilidinin tek denetim noktasıdır (<see cref="IsLockedAsync"/>): onaylı aylık puantaj ayına yazan
/// her kanal <see cref="AttendanceErrors.PeriodLocked"/> alır. Günün planı her kanalda K2 önceliğiyle <see cref="IPlannedDayResolver"/>'dan
/// çözülür (vardiya ataması + tanım kuralları → çalışma çizelgesi → plansız); tanımın kural seti (tolerans, FM min/maks, bonus) güne verilir.
/// Limitler günün kaynağına göre <see cref="LimitPolicy"/>'den gelir. Kaydetme <see cref="SaveAsync"/> ile yapılır: eşzamanlı yazma
/// çakışmasında (günün xmin'i değişti / aynı gün ya da okutma başka işlemde oluştu) dokunulan günler taze veriden yeniden türetilip
/// sınırlı sayıda yeniden denenir (bkz. PunchDayWriter.Save.cs).
/// </summary>
public sealed partial class PunchDayWriter(
    Guid tenantId, IAttendanceDayRepository days, IPunchRepository punches, IPlannedDayResolver planner,
    IMonthlyTimesheetApprovalRepository approvals, TimeZoneInfo zone, DateTime nowUtc)
{
    /// <summary>Taşıma (B1) ve eski günlerden türetilen okutmaların gerekçesi; geri alma (Down) bu değerle süzer.</summary>
    public const string LegacyReason = "Mevcut puantaj gününden taşındı";

    private readonly List<Punch> _pending = [];
    private readonly Dictionary<(Guid EmployeeId, DateOnly Date), AttendanceDay> _createdDays = [];
    private readonly Dictionary<(Guid EmployeeId, DateOnly Month), bool> _locks = [];
    private readonly Dictionary<(Guid EmployeeId, DateOnly Date), PlannedDay?> _windows = [];

    public TimeZoneInfo Zone => zone;

    public DateTime NowUtc => nowUtc;

    /// <summary>K6: çalışanın bu gününün ayı için onaylı aylık puantaj var mı (onaylı ayda okutma eklenmez/silinmez, gün yeniden kurulmaz).</summary>
    public async Task<bool> IsLockedAsync(Guid employeeId, DateOnly workDate, CancellationToken cancellationToken)
    {
        var month = MonthlyTimesheetApproval.NormalizeToMonthStart(workDate);
        if (!_locks.TryGetValue((employeeId, month), out var locked))
        {
            locked = await approvals.FindAsync(employeeId, month, cancellationToken).ConfigureAwait(false) is not null;
            _locks[(employeeId, month)] = locked;
        }

        return locked;
    }

    /// <summary>
    /// Okutmanın puantaj günü adayları (spec §5.1): yerel takvim gününün bir öncesi, kendisi ve sonrası (sonraki günün vardiyası önceki
    /// akşam başlayabilir), her biri K2 planıyla (ilk giriş bilinmeden - tanımın nominal saatleri). Yazıcı ömrü boyunca önbelleklenir.
    /// </summary>
    public async Task<IReadOnlyList<(DateOnly Date, PlannedDay? Plan)>> CandidatesAsync(Guid employeeId, DateOnly localDate, CancellationToken cancellationToken)
    {
        var candidates = new List<(DateOnly Date, PlannedDay? Plan)>(3);
        for (var offset = -1; offset <= 1; offset++)
        {
            var date = localDate.AddDays(offset);
            if (!_windows.TryGetValue((employeeId, date), out var plan))
            {
                plan = await planner.ResolveAsync(employeeId, date, firstIn: null, cancellationToken).ConfigureAwait(false);
                _windows[(employeeId, date)] = plan;
            }

            candidates.Add((date, plan));
        }

        return candidates;
    }

    /// <summary>
    /// Bu yazıcının eklediği ama henüz kaydedilmemiş okutmaları ve günleri izlemeden çıkarır (başarısız toplu alım, tekillik yarışı) - yeniden
    /// denemede gün, yarışı kazanan işlemin kaydettiği satırdan bulunup güncellenir.
    /// </summary>
    public void DiscardPending()
    {
        foreach (var punch in _pending)
        {
            punches.Discard(punch);
        }

        foreach (var day in _createdDays.Values)
        {
            days.Discard(day);
        }

        _pending.Clear();
        _createdDays.Clear();
    }

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

        var legacy = await FindDayAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
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
        if (await IsLockedAsync(employeeId, date, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(AttendanceErrors.PeriodLocked);
        }

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
    /// Günü okutmalardan ve K2 planından yeniden kurar ve <see cref="AttendanceDay"/>'i ekler/günceller. <paramref name="source"/> null ise en
    /// son okutmanın kaynağı (okutma yoksa günün mevcut kaynağı) kullanılır. <paramref name="limits"/> kiracı limitleridir; güne uygulanıp
    /// uygulanmayacağına günün kaynağıyla <see cref="LimitPolicy"/> karar verir. Okutması olan gün şu durumlarda Incalculable'dır (spec §5.1):
    /// okutmalar tutarsız ya da plan yok (<see cref="DayFromPunches"/>), dinamik vardiya eşitliği (<see cref="PlannedDay.IsAmbiguous"/>), bir
    /// okutma iki vardiya başlangıcına eşit uzaklıkta (gece vardiyası çakışması, <see cref="WorkDateResolver"/>) ya da çağıran zorlar
    /// (<paramref name="forceIncalculable"/>).
    /// </summary>
    public async Task<Result<AttendanceDay>> RebuildAsync(
        Guid employeeId, DateOnly workDate, AttendanceDaySource? source, ShiftLimits? limits, bool forceIncalculable, CancellationToken cancellationToken)
    {
        if (await IsLockedAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict(AttendanceErrors.PeriodLocked);
        }

        var dayPunches = await LiveAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
        var plan = await planner.ResolveAsync(employeeId, workDate, DayFromPunches.FirstIn(dayPunches, zone), cancellationToken).ConfigureAwait(false);
        var result = DayFromPunches.Build(dayPunches, plan, zone);
        var planned = plan ?? PlannedDay.NonWorking();
        var existing = await FindDayAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);
        var daySource = source
            ?? (dayPunches.Count > 0 ? PunchTime.ToDaySource(dayPunches[^1].Source) : existing?.Source ?? AttendanceDaySource.Manual);
        _rebuilt[(employeeId, workDate)] = new RebuildRequest(source, limits, forceIncalculable);

        // Kanalın limit kuralı + vardiya tanımının kural seti (kural yoksa eski hesap birebir).
        var channelLimits = LimitPolicy.For(daySource, limits);
        var dayLimits = planned.Rules is { } rules ? (channelLimits ?? ShiftLimits.None) with { Rules = rules } : channelLimits;

        AttendanceDay day;
        if (existing is null)
        {
            day = AttendanceDay.Record(
                tenantId, employeeId, workDate, result.CheckIn, result.CheckOut, plannedStart: planned.Start, plannedEnd: planned.End,
                plannedMinutes: planned.PlannedMinutes, breakMinutes: planned.BreakMinutes, source: daySource, overnight: result.Overnight,
                actualBreakMinutes: result.ActualBreakMinutes, shiftLimits: dayLimits);
            days.Add(day);
            _createdDays[(employeeId, workDate)] = day;
        }
        else
        {
            var updated = existing.UpdateRecord(
                result.CheckIn, result.CheckOut, plannedStart: planned.Start, plannedEnd: planned.End, plannedMinutes: planned.PlannedMinutes,
                breakMinutes: planned.BreakMinutes, source: daySource, overnight: result.Overnight, actualBreakMinutes: result.ActualBreakMinutes,
                shiftLimits: dayLimits);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            day = existing;
        }

        if (result.Incalculable || (dayPunches.Count > 0 && (forceIncalculable || planned.IsAmbiguous
            || await HasNightShiftOverlapAsync(employeeId, dayPunches, cancellationToken).ConfigureAwait(false))))
        {
            day.MarkIncalculable();
        }

        return day;
    }

    /// <summary>Gece vardiyası çakışması (spec §5.1): günün bir okutması iki aday günün vardiya başlangıcına eşit uzaklıkta.</summary>
    private async Task<bool> HasNightShiftOverlapAsync(Guid employeeId, IReadOnlyList<Punch> dayPunches, CancellationToken cancellationToken)
    {
        foreach (var punch in dayPunches)
        {
            var local = PunchTime.ToLocal(punch.OccurredAtUtc, zone);
            var candidates = await CandidatesAsync(employeeId, DateOnly.FromDateTime(local), cancellationToken).ConfigureAwait(false);
            if (WorkDateResolver.ResolveDetailed(local, candidates).Ambiguous)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Kayıtlı gün ya da bu yazıcının az önce eklediği (henüz kaydedilmemiş) gün - aynı gün iki kez eklenmez.</summary>
    public async Task<AttendanceDay?> FindDayAsync(Guid employeeId, DateOnly workDate, CancellationToken cancellationToken) =>
        _createdDays.TryGetValue((employeeId, workDate), out var created)
            ? created
            : await days.FindByEmployeeAndDateAsync(employeeId, workDate, cancellationToken).ConfigureAwait(false);

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
