using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class ShiftAssignmentDomainTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);

    [Fact]
    public void Create_DayShift_ComputesShiftMinutes()
    {
        var created = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, isNight: false, note: null);

        created.IsSuccess.ShouldBeTrue();
        created.Value.ShiftMinutes.ShouldBe(8 * 60 - 30);
    }

    [Fact]
    public void Create_OvernightShift_CrossesMidnightCorrectly()
    {
        // 22:00 - 06:00 vardiyası: 8 saat, gece yarısını geçiyor.
        var created = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(22, 0), new TimeOnly(6, 0), breakMinutes: 0, isNight: true, note: null);

        created.IsSuccess.ShouldBeTrue();
        created.Value.ShiftMinutes.ShouldBe(8 * 60);
    }

    [Fact]
    public void Create_StartEqualsEnd_Fails()
    {
        var created = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), new TimeOnly(9, 0), 0, false, null);

        created.IsFailure.ShouldBeTrue();
        created.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftTimeRangeInvalid);
    }

    [Fact]
    public void Publish_DraftAssignment_SetsPublishedStatus()
    {
        var assignment = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, "Not").Value;

        var result = assignment.Publish(DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        assignment.Status.ShouldBe(ShiftAssignmentStatus.Published);
        assignment.PublishedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Publish_AlreadyPublished_Fails()
    {
        var assignment = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null).Value;
        assignment.Publish(DateTime.UtcNow);

        var result = assignment.Publish(DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftAlreadyPublished);
    }

    [Fact]
    public void Update_ChangesTimesAndNote()
    {
        var assignment = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), new TimeOnly(17, 0), 0, false, null).Value;

        var result = assignment.Update(new TimeOnly(10, 0), new TimeOnly(18, 0), 30, true, "Güncellendi");

        result.IsSuccess.ShouldBeTrue();
        assignment.Start.ShouldBe(new TimeOnly(10, 0));
        assignment.IsNight.ShouldBeTrue();
        assignment.Note.ShouldBe("Güncellendi");
    }
}

public sealed class ShiftPlanLockDomainTests
{
    private static readonly DateOnly SomeDayInMonth = new(2026, 9, 15);

    [Fact]
    public void Lock_NormalizesPeriodToMonthStart()
    {
        var lockRow = ShiftPlanLock.Lock(Guid.NewGuid(), SomeDayInMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);

        lockRow.PeriodMonth.ShouldBe(new DateOnly(2026, 9, 1));
        lockRow.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Unlock_LockedPeriod_RequiresReason()
    {
        var lockRow = ShiftPlanLock.Lock(Guid.NewGuid(), SomeDayInMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);

        var result = lockRow.Unlock(DateTime.UtcNow, Guid.NewGuid(), reason: "");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftUnlockReasonRequired);
        lockRow.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Unlock_LockedPeriod_WithReason_Succeeds()
    {
        var lockRow = ShiftPlanLock.Lock(Guid.NewGuid(), SomeDayInMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);

        var result = lockRow.Unlock(DateTime.UtcNow, Guid.NewGuid(), "Yanlışlıkla kilitlendi");

        result.IsSuccess.ShouldBeTrue();
        lockRow.IsLocked.ShouldBeFalse();
        lockRow.UnlockReason.ShouldBe("Yanlışlıkla kilitlendi");
    }

    [Fact]
    public void Unlock_AlreadyUnlocked_Fails()
    {
        var lockRow = ShiftPlanLock.Lock(Guid.NewGuid(), SomeDayInMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);
        lockRow.Unlock(DateTime.UtcNow, Guid.NewGuid(), "sebep");

        var result = lockRow.Unlock(DateTime.UtcNow, Guid.NewGuid(), "sebep");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftPeriodNotLocked);
    }

    [Fact]
    public void Relock_UnlockedPeriod_LocksAgain()
    {
        var lockRow = ShiftPlanLock.Lock(Guid.NewGuid(), SomeDayInMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);
        lockRow.Unlock(DateTime.UtcNow, Guid.NewGuid(), "sebep");

        var result = lockRow.Relock(DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);

        result.IsSuccess.ShouldBeTrue();
        lockRow.IsLocked.ShouldBeTrue();
        lockRow.UnlockReason.ShouldBeNull();
    }

    [Fact]
    public void Relock_AlreadyLocked_Fails()
    {
        var lockRow = ShiftPlanLock.Lock(Guid.NewGuid(), SomeDayInMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);

        var result = lockRow.Relock(DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftPeriodAlreadyLocked);
    }
}

public sealed class ShiftSettingsDomainTests
{
    [Fact]
    public void CreateDefault_MatchesLegalDefaults()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        settings.MaxDailyMinutes.ShouldBe(11 * 60);
        settings.MaxWeeklyMinutes.ShouldBe(45 * 60);
        settings.AutoLockEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Update_NightWorkEnabledWithoutTimes_Fails()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.Update(660, 6, 2700, nightWorkEnabled: true, null, null, null, 30, false, false, 26, 3);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftSettingsInvalid);
    }

    [Fact]
    public void Update_ValidValues_Applies()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.Update(360, 2, 900, false, null, null, null, 30, true, true, 26, 3);

        result.IsSuccess.ShouldBeTrue();
        settings.MaxDailyMinutes.ShouldBe(360);
        settings.MaxWeeklyDays.ShouldBe(2);
        settings.AutoLockEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Update_InvalidWeeklyDays_Fails()
    {
        var settings = ShiftSettings.CreateDefault(Guid.NewGuid());

        var result = settings.Update(660, 8, 2700, false, null, null, null, 30, false, false, 26, 3);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftSettingsInvalid);
    }
}
