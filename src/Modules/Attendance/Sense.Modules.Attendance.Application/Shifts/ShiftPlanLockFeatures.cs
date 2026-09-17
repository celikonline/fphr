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

/// <summary>
/// Aylık puantaj kapanışı, otomatik kilit (PO tur 1 kapanışı 17:45 "aylık puantaj kapanışı önce" + TALIMAT
/// faz-06 md.8, <see cref="ShiftPlanLockSource.Auto"/> - şimdiye dek "package A4 scope" olarak ayrılmıştı):
/// önceki takvim ayı, ayarın <see cref="ShiftSettings.GetScheduledLockDate"/>'ine göre vadesi gelmişse kilitlenir. Sistem komutu - kasıtlı
/// olarak [RequiresPermission] yok (job/tenant döngüsünden çağrılır, AuthorizationBehaviour attribute'suz istekleri
/// olduğu gibi geçirir). Zamanlayıcı kaydı bu turun kapsamı dışında (BackgroundJobSeeding.cs/JobTypes.cs sıcak
/// dosya, yalnız faz-01 BE) - bu handler'ı hangi recurring job'un ne zaman çağıracağı PO/faz-01 kararı.
/// İdempotent: dönem için satır zaten varsa (otomatik/elle kilitli ya da elle açılmış) dokunmaz - elle açılan bir
/// dönemi asla kendiliğinden yeniden kilitlemez.
/// </summary>
public sealed record LockDueShiftPeriodsCommand(DateOnly Today) : ICommand<bool>;

public sealed class LockDueShiftPeriodsHandler(IShiftPlanLockRepository locks, IShiftSettingsRepository settings, ITenantContext tenant, TimeProvider clock)
    : ICommandHandler<LockDueShiftPeriodsCommand, bool>
{
    public async Task<Result<bool>> Handle(LockDueShiftPeriodsCommand command, CancellationToken cancellationToken)
    {
        var settingsRow = await settings.FindAsync(cancellationToken);
        if (settingsRow is null || !settingsRow.AutoLockEnabled)
        {
            return false;
        }

        var previousMonth = ShiftPlanLock.NormalizeToMonthStart(command.Today).AddMonths(-1);
        var scheduledLockDate = settingsRow.GetScheduledLockDate(previousMonth);
        if (scheduledLockDate is null || command.Today < scheduledLockDate.Value)
        {
            return false;
        }

        var existing = await locks.FindByPeriodAsync(previousMonth, cancellationToken);
        if (existing is not null)
        {
            return false;
        }

        locks.Add(ShiftPlanLock.Lock(tenant.TenantId, previousMonth, clock.GetUtcNow().UtcDateTime, null, ShiftPlanLockSource.Auto));
        return true;
    }
}
