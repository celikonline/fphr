using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Shifts;

/// <summary>
/// Vardiya ayarları (11.3.6 "Uygulamalar &gt; Vardiya yönetimi &gt; Ayarlar"): one row per tenant, lazily seeded like
/// <see cref="WorkSchedules.WorkSchedule"/>'s default. MVP simplification: a single limit set, not the spec's
/// Tam zamanlı/Yarı zamanlı split (<see cref="Sense.Modules.Attendance.Domain"/> doc) - differentiating by employment
/// type needs a new Employee.Contracts read port, deliberately deferred to avoid touching Employee's IEmployeeLookup
/// while another concurrent session was mid-edit on it (package A4 session note). Auto-lock scheduling
/// (<see cref="AutoLockEnabled"/>/<see cref="AutoLockDayOfMonth"/>) is read by callers to compute the next lock date;
/// no background job locks periods automatically yet (<see cref="Shifts.ShiftPlanLock"/> only records actual locks).
/// </summary>
public sealed class ShiftSettings : TenantAggregateRoot<Guid>
{
    public const int DefaultMaxDailyMinutes = 11 * 60;
    public const int DefaultMaxWeeklyDays = 6;
    public const int DefaultMaxWeeklyMinutes = 45 * 60;
    public const int DefaultBreakMinMinutes = 30;

    /// <summary>İş K. günlük kesintisiz dinlenme: iki vardiya arasında en az 11 saat.</summary>
    public const int MinDailyRestMinutes = 11 * 60;

    /// <summary>m.69 gece çalışması üst sınırı: 7,5 saat.</summary>
    public const int DefaultNightWorkMaxMinutes = 450;

    public static readonly TimeOnly DefaultNightWorkStart = new(20, 0);

    public static readonly TimeOnly DefaultNightWorkEnd = new(6, 0);

    /// <summary>m.63 denkleştirme: varsayılan dönem (yazılı anlaşma olmadan) 2 ay.</summary>
    public const int DefaultEqualizationPeriodMonths = 2;

    /// <summary>m.63 denkleştirme: yazılı anlaşmayla uzatılabilen azami dönem, 4 ay.</summary>
    public const int ExtendedEqualizationPeriodMonths = 4;

    private ShiftSettings()
    {
    }

    private ShiftSettings(
        Guid id, Guid tenantId, int maxDailyMinutes, int maxWeeklyDays, int maxWeeklyMinutes, bool nightWorkEnabled, TimeOnly? nightWorkStart,
        TimeOnly? nightWorkEnd, int? nightWorkMaxMinutes, int breakMinMinutes, bool breakIsPaid, bool autoLockEnabled, int autoLockDayOfMonth,
        int autoLockNotifyDaysBefore)
        : base(id, tenantId)
    {
        MaxDailyMinutes = maxDailyMinutes;
        MaxWeeklyDays = maxWeeklyDays;
        MaxWeeklyMinutes = maxWeeklyMinutes;
        NightWorkEnabled = nightWorkEnabled;
        NightWorkStart = nightWorkStart;
        NightWorkEnd = nightWorkEnd;
        NightWorkMaxMinutes = nightWorkMaxMinutes;
        BreakMinMinutes = breakMinMinutes;
        BreakIsPaid = breakIsPaid;
        AutoLockEnabled = autoLockEnabled;
        AutoLockDayOfMonth = autoLockDayOfMonth;
        AutoLockNotifyDaysBefore = autoLockNotifyDaysBefore;
        EqualizationPeriodMonths = DefaultEqualizationPeriodMonths;
        EqualizationAnchorMonth = 1;
    }

    /// <summary>"Günlük çalışma süresi üst sınırı" (varsayılan 11 saat).</summary>
    public int MaxDailyMinutes { get; private set; }

    /// <summary>"Haftalık çalışma günü".</summary>
    public int MaxWeeklyDays { get; private set; }

    /// <summary>"Haftalık çalışma saati" (varsayılan 45 saat).</summary>
    public int MaxWeeklyMinutes { get; private set; }

    public bool NightWorkEnabled { get; private set; }

    public TimeOnly? NightWorkStart { get; private set; }

    public TimeOnly? NightWorkEnd { get; private set; }

    public int? NightWorkMaxMinutes { get; private set; }

    public int BreakMinMinutes { get; private set; }

    public bool BreakIsPaid { get; private set; }

    public bool AutoLockEnabled { get; private set; }

    /// <summary>Day of the month a period locks on, once past its own month (e.g. 26 → "26 Ağustos'ta kilitlenecek").</summary>
    public int AutoLockDayOfMonth { get; private set; }

    public int AutoLockNotifyDaysBefore { get; private set; }

    /// <summary>m.63 denkleştirme uygulanıyor mu (kart senseik-014 §4); kapalıyken günlük <c>OvertimeMinutes</c> doğrudan kesindir.</summary>
    public bool EqualizationEnabled { get; private set; }

    /// <summary>2 (varsayılan) veya 4 (<see cref="HasWrittenEqualizationAgreement"/> zorunlu) ay.</summary>
    public int EqualizationPeriodMonths { get; private set; }

    /// <summary>Dönem döngüsünün başladığı takvim ayı (1-12) - <c>Equalization.EqualizationPeriodCalculator</c>'a girdi.</summary>
    public int EqualizationAnchorMonth { get; private set; }

    /// <summary>m.63: dönem 4 aya çıkarılmışsa yazılı anlaşma şartı karşılandı mı.</summary>
    public bool HasWrittenEqualizationAgreement { get; private set; }

    /// <summary>
    /// Yeni tenant varsayılanı (kart senseik-014 §3): gece çalışması AÇIK, 20:00-06:00 penceresi, 7,5 saat üst sınır -
    /// Leave modülündeki sabit gece penceresiyle (<c>Overtime.OvertimeMath</c>, md.69) bilinçli olarak aynı. Mevcut
    /// tenantların satırları bu değişiklikten ETKİLENMEZ (yalnız yeni <c>CreateDefault</c> çağrıları); bkz. handoff
    /// notu "Implementasyon Notu" - eski satırları güncellemek ayrı, bilinçli bir veri geçiş kararı gerektirir.
    /// </summary>
    public static ShiftSettings CreateDefault(Guid tenantId) =>
        new(Guid.CreateVersion7(), tenantId, DefaultMaxDailyMinutes, DefaultMaxWeeklyDays, DefaultMaxWeeklyMinutes, nightWorkEnabled: true,
            DefaultNightWorkStart, DefaultNightWorkEnd, DefaultNightWorkMaxMinutes, DefaultBreakMinMinutes, breakIsPaid: false, autoLockEnabled: false,
            autoLockDayOfMonth: 26, autoLockNotifyDaysBefore: 3);

    /// <summary>
    /// Sunucu tarafı gece vardiyası tespiti (m.69): gece çalışması açıksa ve vardiya süresinin yarısından fazlası gece penceresine
    /// (varsayılan 20:00-06:00) düşüyorsa gece vardiyasıdır. İstemcinin gönderdiği <c>IsNight</c> bayrağı tek başına güvenilmez.
    /// </summary>
    public bool IsNightShift(TimeOnly start, TimeOnly end)
    {
        if (!NightWorkEnabled || NightWorkStart is not { } nightStart || NightWorkEnd is not { } nightEnd)
        {
            return false;
        }

        var span = end.ToTimeSpan() - start.ToTimeSpan();
        if (span <= TimeSpan.Zero)
        {
            span += TimeSpan.FromDays(1);
        }

        var grossMinutes = (int)span.TotalMinutes;
        return DailyWorkCalculator.NightOverlapMinutes(start, grossMinutes, nightStart, nightEnd) * 2 > grossMinutes;
    }

    public Result Update(
        int maxDailyMinutes, int maxWeeklyDays, int maxWeeklyMinutes, bool nightWorkEnabled, TimeOnly? nightWorkStart, TimeOnly? nightWorkEnd,
        int? nightWorkMaxMinutes, int breakMinMinutes, bool breakIsPaid, bool autoLockEnabled, int autoLockDayOfMonth, int autoLockNotifyDaysBefore)
    {
        if (maxDailyMinutes <= 0 || maxWeeklyMinutes <= 0 || maxWeeklyDays is <= 0 or > AttendanceLimits.DaysPerWeek || breakMinMinutes < 0)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftSettingsInvalid);
        }

        if (nightWorkEnabled && (nightWorkStart is null || nightWorkEnd is null))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftSettingsInvalid);
        }

        if (autoLockEnabled && autoLockDayOfMonth is < 1 or > 28)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftSettingsInvalid);
        }

        MaxDailyMinutes = maxDailyMinutes;
        MaxWeeklyDays = maxWeeklyDays;
        MaxWeeklyMinutes = maxWeeklyMinutes;
        NightWorkEnabled = nightWorkEnabled;
        NightWorkStart = nightWorkEnabled ? nightWorkStart : null;
        NightWorkEnd = nightWorkEnabled ? nightWorkEnd : null;
        NightWorkMaxMinutes = nightWorkEnabled ? nightWorkMaxMinutes : null;
        BreakMinMinutes = breakMinMinutes;
        BreakIsPaid = breakIsPaid;
        AutoLockEnabled = autoLockEnabled;
        AutoLockDayOfMonth = autoLockDayOfMonth;
        AutoLockNotifyDaysBefore = autoLockNotifyDaysBefore;
        return Result.Success();
    }

    /// <summary>
    /// Denkleştirme ayarlarını günceller (m.63, kart senseik-014 §4). <paramref name="periodMonths"/> yalnız 2 veya 4
    /// olabilir; 4 seçildiğinde <paramref name="hasWrittenAgreement"/> true olmalıdır (md.63: "yazılı anlaşma ile dört
    /// aya kadar artırılabilir"). <paramref name="anchorMonth"/> dönem döngüsünün başladığı takvim ayıdır (1-12).
    /// </summary>
    public Result UpdateEqualization(bool enabled, int periodMonths, int anchorMonth, bool hasWrittenAgreement)
    {
        if (enabled)
        {
            if (periodMonths != DefaultEqualizationPeriodMonths && periodMonths != ExtendedEqualizationPeriodMonths)
            {
                return Error.Validation(AttendanceDomainErrors.EqualizationPeriodMonthsInvalid);
            }

            if (periodMonths == ExtendedEqualizationPeriodMonths && !hasWrittenAgreement)
            {
                return Error.Validation(AttendanceDomainErrors.EqualizationWrittenAgreementRequired);
            }

            if (anchorMonth is < 1 or > 12)
            {
                return Error.Validation(AttendanceDomainErrors.EqualizationAnchorMonthInvalid);
            }
        }

        EqualizationEnabled = enabled;
        EqualizationPeriodMonths = periodMonths;
        EqualizationAnchorMonth = anchorMonth;
        HasWrittenEqualizationAgreement = hasWrittenAgreement;
        return Result.Success();
    }

    /// <summary>
    /// Otomatik kilit tarihi (11.3.6 "26 Ağustos tarihinde kilitlenecek"): <paramref name="periodMonth"/> ayın
    /// herhangi bir günü olabilir, ay başına normalize edilir (<see cref="ShiftPlanLock.NormalizeToMonthStart"/>).
    /// <see cref="AutoLockEnabled"/> kapalıysa <c>null</c>; açıksa dönemin **bir sonraki ayının**
    /// <see cref="AutoLockDayOfMonth"/> günü döner - o ay bu günü içermiyorsa (kısa ay/Şubat) ayın son günü kullanılır.
    /// </summary>
    public DateOnly? GetScheduledLockDate(DateOnly periodMonth)
    {
        if (!AutoLockEnabled)
        {
            return null;
        }

        var nextMonth = ShiftPlanLock.NormalizeToMonthStart(periodMonth).AddMonths(1);
        var day = Math.Min(AutoLockDayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
        return new DateOnly(nextMonth.Year, nextMonth.Month, day);
    }
}
