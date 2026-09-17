using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Shifts;

public enum ShiftPlanLockSource
{
    Manual = 0,
    Auto = 1,
}

/// <summary>
/// Records that a calendar month's shift plan is locked (11.3.6 "26 Ağustos tarihinde kilitlenecek" / plan kilidi):
/// one row per (tenant, <see cref="PeriodMonth"/>) once it has been locked at least once. No row = the period was
/// never locked. <see cref="ShiftPlanLockSource.Auto"/> is written by <c>LockDueShiftPeriodsCommand</c> (Application
/// layer, called from a tenant-scoped job context) once a recurring job schedule calls it (job registration is
/// faz-01 BE's `BackgroundJobSeeding.cs`/`JobTypes.cs`, not yet wired); manual lock/unlock (attendance.shift.lock/.unlock)
/// creates or flips these rows the rest of the time.
/// </summary>
public sealed class ShiftPlanLock : TenantAggregateRoot<Guid>
{
    private ShiftPlanLock()
    {
    }

    private ShiftPlanLock(Guid id, Guid tenantId, DateOnly periodMonth, DateTime lockedAt, Guid? lockedByUserId, ShiftPlanLockSource source)
        : base(id, tenantId)
    {
        PeriodMonth = periodMonth;
        LockedAt = lockedAt;
        LockedByUserId = lockedByUserId;
        Source = source;
    }

    /// <summary>Normalized to the first day of the month.</summary>
    public DateOnly PeriodMonth { get; private set; }

    public DateTime LockedAt { get; private set; }

    public Guid? LockedByUserId { get; private set; }

    public ShiftPlanLockSource Source { get; private set; }

    public DateTime? UnlockedAt { get; private set; }

    public Guid? UnlockedByUserId { get; private set; }

    public string? UnlockReason { get; private set; }

    public bool IsLocked => UnlockedAt is null;

    public static ShiftPlanLock Lock(Guid tenantId, DateOnly periodMonth, DateTime lockedAt, Guid? lockedByUserId, ShiftPlanLockSource source) =>
        new(Guid.CreateVersion7(), tenantId, NormalizeToMonthStart(periodMonth), lockedAt, lockedByUserId, source);

    /// <summary>Re-lock a previously unlocked period (same row, per-period history stays in one place).</summary>
    public Result Relock(DateTime lockedAt, Guid? lockedByUserId, ShiftPlanLockSource source)
    {
        if (IsLocked)
        {
            return Error.Conflict(AttendanceDomainErrors.ShiftPeriodAlreadyLocked);
        }

        LockedAt = lockedAt;
        LockedByUserId = lockedByUserId;
        Source = source;
        UnlockedAt = null;
        UnlockedByUserId = null;
        UnlockReason = null;
        return Result.Success();
    }

    /// <summary>"attendance.shift.unlock" istisnai açma - audit için sebep zorunlu.</summary>
    public Result Unlock(DateTime unlockedAt, Guid? unlockedByUserId, string reason)
    {
        if (!IsLocked)
        {
            return Error.Conflict(AttendanceDomainErrors.ShiftPeriodNotLocked);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation(AttendanceDomainErrors.ShiftUnlockReasonRequired);
        }

        UnlockedAt = unlockedAt;
        UnlockedByUserId = unlockedByUserId;
        UnlockReason = reason;
        return Result.Success();
    }

    public static DateOnly NormalizeToMonthStart(DateOnly date) => new(date.Year, date.Month, 1);
}
