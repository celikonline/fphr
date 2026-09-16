using FluentValidation;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Shifts;

/// <summary>
/// <see cref="ScheduledLockDate"/> (TALIMAT faz-06 backend md.4, <see cref="ShiftSettings.GetScheduledLockDate"/>):
/// ayar kaydı yoksa veya <see cref="ShiftSettings.AutoLockEnabled"/> kapalıysa <c>null</c> - alan JSON'dan düşer,
/// mevcut tüketiciler kırılmaz.
/// </summary>
public sealed record ShiftPlanLockStatusDto(DateOnly PeriodMonth, bool IsLocked, DateTime? LockedAt, DateTime? UnlockedAt, string? UnlockReason, DateOnly? ScheduledLockDate);

internal static class ShiftPlanLockMapper
{
    public static ShiftPlanLockStatusDto ToDto(DateOnly periodMonth, ShiftPlanLock? plan, DateOnly? scheduledLockDate) =>
        new(periodMonth, plan?.IsLocked ?? false, plan?.IsLocked == true ? plan.LockedAt : null, plan?.UnlockedAt, plan?.UnlockReason, scheduledLockDate);
}

/// <summary>Aylık görünümdeki kilit bandı (11.3.6 "26 Ağustos tarihinde kilitlenecek").</summary>
[RequiresPermission("attendance.shift.view")]
public sealed record GetShiftPlanLockStatusQuery(DateOnly PeriodMonth) : IQuery<ShiftPlanLockStatusDto>;

public sealed class GetShiftPlanLockStatusHandler(IShiftPlanLockRepository locks, IShiftSettingsRepository settings)
    : IQueryHandler<GetShiftPlanLockStatusQuery, ShiftPlanLockStatusDto>
{
    public async Task<Result<ShiftPlanLockStatusDto>> Handle(GetShiftPlanLockStatusQuery query, CancellationToken cancellationToken)
    {
        var normalized = ShiftPlanLock.NormalizeToMonthStart(query.PeriodMonth);
        var plan = await locks.FindByPeriodAsync(normalized, cancellationToken);
        var settingsRow = await settings.FindAsync(cancellationToken);
        var scheduledLockDate = settingsRow?.GetScheduledLockDate(normalized);
        return ShiftPlanLockMapper.ToDto(normalized, plan, scheduledLockDate);
    }
}

[RequiresPermission("attendance.shift.lock")]
public sealed record LockShiftPeriodCommand(DateOnly PeriodMonth) : ICommand;

public sealed class LockShiftPeriodHandler(IShiftPlanLockRepository locks, ITenantContext tenant, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<LockShiftPeriodCommand>
{
    public async Task<Result> Handle(LockShiftPeriodCommand command, CancellationToken cancellationToken)
    {
        var normalized = ShiftPlanLock.NormalizeToMonthStart(command.PeriodMonth);
        var existing = await locks.FindByPeriodAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            return existing.Relock(clock.GetUtcNow().UtcDateTime, currentUser.UserId, ShiftPlanLockSource.Manual);
        }

        locks.Add(ShiftPlanLock.Lock(tenant.TenantId, normalized, clock.GetUtcNow().UtcDateTime, currentUser.UserId, ShiftPlanLockSource.Manual));
        return Result.Success();
    }
}

/// <summary>"attendance.shift.unlock": istisnai açma, denetim için sebep zorunlu.</summary>
[RequiresPermission("attendance.shift.unlock")]
public sealed record UnlockShiftPeriodCommand(DateOnly PeriodMonth, string Reason) : ICommand;

public sealed class UnlockShiftPeriodValidator : AbstractValidator<UnlockShiftPeriodCommand>
{
    public UnlockShiftPeriodValidator() => RuleFor(x => x.Reason).NotEmpty();
}

public sealed class UnlockShiftPeriodHandler(IShiftPlanLockRepository locks, ICurrentUser currentUser, TimeProvider clock) : ICommandHandler<UnlockShiftPeriodCommand>
{
    public async Task<Result> Handle(UnlockShiftPeriodCommand command, CancellationToken cancellationToken)
    {
        var normalized = ShiftPlanLock.NormalizeToMonthStart(command.PeriodMonth);
        var existing = await locks.FindByPeriodAsync(normalized, cancellationToken);
        return existing is null
            ? Error.NotFound(AttendanceDomainErrors.ShiftPeriodNotLocked)
            : existing.Unlock(clock.GetUtcNow().UtcDateTime, currentUser.UserId, command.Reason);
    }
}
