using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// TALIMAT faz-06 backend md.3: <see cref="ShiftSettings.GetScheduledLockDate"/> - autoLockDayOfMonth is capped at
/// 28 by <see cref="ShiftSettings.Update"/> validation, so the "short month" clip inside GetScheduledLockDate is
/// defensive (every month has at least 28 days); these cases still exercise the month/year rollover arithmetic
/// (Şubat/artık yıl, Aralık→Ocak) that the clip depends on.
/// </summary>
public sealed class ShiftSettingsDomainTests
{
    private static ShiftSettings CreateWithAutoLock(bool enabled, int dayOfMonth)
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());
        var result = settings.Update(
            ShiftSettings.DefaultMaxDailyMinutes, ShiftSettings.DefaultMaxWeeklyDays, ShiftSettings.DefaultMaxWeeklyMinutes,
            nightWorkEnabled: false, null, null, null, ShiftSettings.DefaultBreakMinMinutes, breakIsPaid: false,
            autoLockEnabled: enabled, autoLockDayOfMonth: dayOfMonth, autoLockNotifyDaysBefore: 3);
        result.IsSuccess.ShouldBeTrue();
        return settings;
    }

    [Fact]
    public void GetScheduledLockDate_AutoLockDisabled_ReturnsNull()
    {
        var settings = CreateWithAutoLock(enabled: false, dayOfMonth: 26);

        settings.GetScheduledLockDate(new DateOnly(2026, 5, 1)).ShouldBeNull();
    }

    [Fact]
    public void GetScheduledLockDate_NormalMonth_ReturnsNextMonthsDay()
    {
        var settings = CreateWithAutoLock(enabled: true, dayOfMonth: 15);

        // Any day within the period month normalizes the same way (ay başına normalize edilir).
        settings.GetScheduledLockDate(new DateOnly(2026, 5, 20)).ShouldBe(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public void GetScheduledLockDate_FebruaryInLeapYear_LandsOnCorrectDay()
    {
        var settings = CreateWithAutoLock(enabled: true, dayOfMonth: 28);

        // 2028 is a leap year (29-day February); day 28 still fits without clipping.
        settings.GetScheduledLockDate(new DateOnly(2028, 1, 10)).ShouldBe(new DateOnly(2028, 2, 28));
    }

    [Fact]
    public void GetScheduledLockDate_DecemberPeriod_RollsOverToNextJanuary()
    {
        var settings = CreateWithAutoLock(enabled: true, dayOfMonth: 26);

        settings.GetScheduledLockDate(new DateOnly(2026, 12, 5)).ShouldBe(new DateOnly(2027, 1, 26));
    }
}
