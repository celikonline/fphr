using FluentValidation;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Shifts;

public sealed record ShiftSettingsDto(
    int MaxDailyMinutes, int MaxWeeklyDays, int MaxWeeklyMinutes, bool NightWorkEnabled, TimeOnly? NightWorkStart, TimeOnly? NightWorkEnd,
    int? NightWorkMaxMinutes, int BreakMinMinutes, bool BreakIsPaid, bool AutoLockEnabled, int AutoLockDayOfMonth, int AutoLockNotifyDaysBefore);

internal static class ShiftSettingsMapper
{
    public static ShiftSettingsDto ToDto(ShiftSettings s) =>
        new(s.MaxDailyMinutes, s.MaxWeeklyDays, s.MaxWeeklyMinutes, s.NightWorkEnabled, s.NightWorkStart, s.NightWorkEnd, s.NightWorkMaxMinutes,
            s.BreakMinMinutes, s.BreakIsPaid, s.AutoLockEnabled, s.AutoLockDayOfMonth, s.AutoLockNotifyDaysBefore);
}

/// <summary>Ensures the tenant's single ShiftSettings row exists (lazy default seed, mirrors WorkScheduleAssignmentService.EnsureDefaultScheduleAsync).</summary>
internal static class ShiftSettingsProvisioning
{
    public static async Task<ShiftSettings> EnsureAsync(IShiftSettingsRepository settings, ITenantContext tenant, IAttendanceUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var existing = await settings.FindAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var seeded = ShiftSettings.CreateDefault(tenant.TenantId);
        settings.Add(seeded);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return seeded;
    }
}

[RequiresPermission("attendance.shift.view")]
public sealed record GetShiftSettingsQuery : IQuery<ShiftSettingsDto>;

public sealed class GetShiftSettingsHandler(IShiftSettingsRepository settings, ITenantContext tenant, IAttendanceUnitOfWork unitOfWork)
    : IQueryHandler<GetShiftSettingsQuery, ShiftSettingsDto>
{
    public async Task<Result<ShiftSettingsDto>> Handle(GetShiftSettingsQuery query, CancellationToken cancellationToken) =>
        ShiftSettingsMapper.ToDto(await ShiftSettingsProvisioning.EnsureAsync(settings, tenant, unitOfWork, cancellationToken));
}

[RequiresPermission("attendance.shift.settings")]
public sealed record UpdateShiftSettingsCommand(
    int MaxDailyMinutes, int MaxWeeklyDays, int MaxWeeklyMinutes, bool NightWorkEnabled, TimeOnly? NightWorkStart, TimeOnly? NightWorkEnd,
    int? NightWorkMaxMinutes, int BreakMinMinutes, bool BreakIsPaid, bool AutoLockEnabled, int AutoLockDayOfMonth, int AutoLockNotifyDaysBefore) : ICommand;

public sealed class UpdateShiftSettingsValidator : AbstractValidator<UpdateShiftSettingsCommand>
{
    public UpdateShiftSettingsValidator()
    {
        RuleFor(x => x.MaxDailyMinutes).GreaterThan(0);
        RuleFor(x => x.MaxWeeklyMinutes).GreaterThan(0);
        RuleFor(x => x.MaxWeeklyDays).InclusiveBetween(1, AttendanceLimits.DaysPerWeek);
        RuleFor(x => x.BreakMinMinutes).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateShiftSettingsHandler(IShiftSettingsRepository settings, ITenantContext tenant, IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<UpdateShiftSettingsCommand>
{
    public async Task<Result> Handle(UpdateShiftSettingsCommand command, CancellationToken cancellationToken)
    {
        var current = await ShiftSettingsProvisioning.EnsureAsync(settings, tenant, unitOfWork, cancellationToken);
        return current.Update(
            command.MaxDailyMinutes, command.MaxWeeklyDays, command.MaxWeeklyMinutes, command.NightWorkEnabled, command.NightWorkStart, command.NightWorkEnd,
            command.NightWorkMaxMinutes, command.BreakMinMinutes, command.BreakIsPaid, command.AutoLockEnabled, command.AutoLockDayOfMonth,
            command.AutoLockNotifyDaysBefore);
    }
}
