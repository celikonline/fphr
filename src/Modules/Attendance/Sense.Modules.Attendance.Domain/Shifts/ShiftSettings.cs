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

    public static ShiftSettings CreateDefault(Guid tenantId) =>
        new(Guid.CreateVersion7(), tenantId, DefaultMaxDailyMinutes, DefaultMaxWeeklyDays, DefaultMaxWeeklyMinutes, nightWorkEnabled: false, null, null,
            null, DefaultBreakMinMinutes, breakIsPaid: false, autoLockEnabled: false, autoLockDayOfMonth: 26, autoLockNotifyDaysBefore: 3);

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
}
