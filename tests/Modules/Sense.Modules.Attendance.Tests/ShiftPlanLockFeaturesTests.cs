using NSubstitute;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class ShiftPlanLockFeaturesTests
{
    private static readonly DateOnly PeriodMonth = new(2026, 9, 1);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IShiftSettingsRepository _settings = Substitute.For<IShiftSettingsRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public ShiftPlanLockFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.UserId.Returns(_userId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns((ShiftSettings?)null);
    }

    [Fact]
    public async Task Lock_NoExistingRow_CreatesLockedRow()
    {
        _locks.FindByPeriodAsync(PeriodMonth, Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        var handler = new LockShiftPeriodHandler(_locks, _tenant, _currentUser, TimeProvider.System);

        var result = await handler.Handle(new LockShiftPeriodCommand(PeriodMonth), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _locks.Received(1).Add(Arg.Is<ShiftPlanLock>(p => p.IsLocked && p.PeriodMonth == PeriodMonth));
    }

    [Fact]
    public async Task Lock_PreviouslyUnlockedRow_Relocks()
    {
        var existing = ShiftPlanLock.Lock(_tenantId, PeriodMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);
        existing.Unlock(DateTime.UtcNow, Guid.NewGuid(), "test");
        _locks.FindByPeriodAsync(PeriodMonth, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new LockShiftPeriodHandler(_locks, _tenant, _currentUser, TimeProvider.System);

        var result = await handler.Handle(new LockShiftPeriodCommand(PeriodMonth), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.IsLocked.ShouldBeTrue();
        _locks.DidNotReceive().Add(Arg.Any<ShiftPlanLock>());
    }

    [Fact]
    public async Task Unlock_NoExistingRow_ReturnsNotFound()
    {
        _locks.FindByPeriodAsync(PeriodMonth, Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        var handler = new UnlockShiftPeriodHandler(_locks, _currentUser, TimeProvider.System);

        var result = await handler.Handle(new UnlockShiftPeriodCommand(PeriodMonth, "sebep"), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftPeriodNotLocked);
    }

    [Fact]
    public async Task Unlock_LockedRow_Unlocks()
    {
        var existing = ShiftPlanLock.Lock(_tenantId, PeriodMonth, DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual);
        _locks.FindByPeriodAsync(PeriodMonth, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new UnlockShiftPeriodHandler(_locks, _currentUser, TimeProvider.System);

        var result = await handler.Handle(new UnlockShiftPeriodCommand(PeriodMonth, "Yanlışlıkla kilitlendi"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.IsLocked.ShouldBeFalse();
        existing.UnlockReason.ShouldBe("Yanlışlıkla kilitlendi");
    }

    [Fact]
    public async Task GetStatus_NoRow_ReturnsUnlocked()
    {
        _locks.FindByPeriodAsync(PeriodMonth, Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        var handler = new GetShiftPlanLockStatusHandler(_locks, _settings);

        var result = await handler.Handle(new GetShiftPlanLockStatusQuery(PeriodMonth), TestContext.Current.CancellationToken);

        result.Value.IsLocked.ShouldBeFalse();
    }

    // TALIMAT faz-06 backend md.4: ScheduledLockDate is filled from ShiftSettings.GetScheduledLockDate when the
    // settings row exists and auto-lock is enabled.
    [Fact]
    public async Task GetStatus_AutoLockEnabled_FillsScheduledLockDate()
    {
        _locks.FindByPeriodAsync(PeriodMonth, Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        var settings = ShiftSettings.CreateDefault(_tenantId);
        settings.Update(
            ShiftSettings.DefaultMaxDailyMinutes, ShiftSettings.DefaultMaxWeeklyDays, ShiftSettings.DefaultMaxWeeklyMinutes,
            nightWorkEnabled: false, null, null, null, ShiftSettings.DefaultBreakMinMinutes, breakIsPaid: false,
            autoLockEnabled: true, autoLockDayOfMonth: 26, autoLockNotifyDaysBefore: 3);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(settings);
        var handler = new GetShiftPlanLockStatusHandler(_locks, _settings);

        var result = await handler.Handle(new GetShiftPlanLockStatusQuery(PeriodMonth), TestContext.Current.CancellationToken);

        result.Value.ScheduledLockDate.ShouldBe(new DateOnly(2026, 10, 26));
    }

    // TALIMAT faz-06 backend "aylık puantaj kapanışı" md.8: LockDueShiftPeriodsCommand - system command, no
    // scheduler wired yet, called once per tenant with "today" by a future recurring job.
    private static ShiftSettings AutoLockSettings(Guid tenantId, int autoLockDayOfMonth = 26)
    {
        var settings = ShiftSettings.CreateDefault(tenantId);
        settings.Update(
            ShiftSettings.DefaultMaxDailyMinutes, ShiftSettings.DefaultMaxWeeklyDays, ShiftSettings.DefaultMaxWeeklyMinutes,
            nightWorkEnabled: false, null, null, null, ShiftSettings.DefaultBreakMinMinutes, breakIsPaid: false,
            autoLockEnabled: true, autoLockDayOfMonth: autoLockDayOfMonth, autoLockNotifyDaysBefore: 3);
        return settings;
    }

    [Fact]
    public async Task LockDuePeriods_NotYetDue_DoesNothing()
    {
        var settings = AutoLockSettings(_tenantId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(settings);
        // Previous month is August; auto-lock day 26 → scheduled for 26 September, today is one day short.
        var handler = new LockDueShiftPeriodsHandler(_locks, _settings, _tenant, TimeProvider.System);

        var result = await handler.Handle(new LockDueShiftPeriodsCommand(new DateOnly(2026, 9, 25)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        _locks.DidNotReceive().Add(Arg.Any<ShiftPlanLock>());
    }

    [Fact]
    public async Task LockDuePeriods_Due_LocksPreviousMonthAsAuto()
    {
        var settings = AutoLockSettings(_tenantId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(settings);
        _locks.FindByPeriodAsync(new DateOnly(2026, 8, 1), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        var handler = new LockDueShiftPeriodsHandler(_locks, _settings, _tenant, TimeProvider.System);

        var result = await handler.Handle(new LockDueShiftPeriodsCommand(new DateOnly(2026, 9, 26)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        _locks.Received(1).Add(Arg.Is<ShiftPlanLock>(p =>
            p.PeriodMonth == new DateOnly(2026, 8, 1) && p.IsLocked && p.Source == ShiftPlanLockSource.Auto && p.LockedByUserId == null));
    }

    [Fact]
    public async Task LockDuePeriods_AlreadyManuallyUnlocked_NeverReLocks()
    {
        var settings = AutoLockSettings(_tenantId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(settings);
        var existing = ShiftPlanLock.Lock(_tenantId, new DateOnly(2026, 8, 1), DateTime.UtcNow, _userId, ShiftPlanLockSource.Auto);
        existing.Unlock(DateTime.UtcNow, _userId, "İK düzeltmesi için açıldı");
        _locks.FindByPeriodAsync(new DateOnly(2026, 8, 1), Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new LockDueShiftPeriodsHandler(_locks, _settings, _tenant, TimeProvider.System);

        var result = await handler.Handle(new LockDueShiftPeriodsCommand(new DateOnly(2026, 9, 26)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        existing.IsLocked.ShouldBeFalse();
        _locks.DidNotReceive().Add(Arg.Any<ShiftPlanLock>());
    }

    [Fact]
    public async Task LockDuePeriods_AutoLockDisabled_DoesNothing()
    {
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(ShiftSettings.CreateDefault(_tenantId));
        var handler = new LockDueShiftPeriodsHandler(_locks, _settings, _tenant, TimeProvider.System);

        var result = await handler.Handle(new LockDueShiftPeriodsCommand(new DateOnly(2026, 9, 26)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        _locks.DidNotReceive().Add(Arg.Any<ShiftPlanLock>());
    }
}
