using FluentValidation;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Shifts;

public sealed record ShiftAssignmentDto(
    Guid Id, Guid EmployeeId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, bool IsNight, string? Note,
    ShiftAssignmentStatus Status, DateTime? PublishedAt, int ShiftMinutes);

internal static class ShiftAssignmentMapper
{
    public static ShiftAssignmentDto ToDto(ShiftAssignment a) =>
        new(a.Id, a.EmployeeId, a.Date, a.Start, a.End, a.BreakMinutes, a.IsNight, a.Note, a.Status, a.PublishedAt, a.ShiftMinutes);
}

/// <summary>
/// Vardiya limitleri (11.3.6 "genel merkezin tanımladığı üst sınırların üzerine çıkamaz"): daily limit (night limit
/// when the shift is marked IsNight and a night limit is configured) + weekly hours/days across the ISO week
/// containing the candidate's date. MVP simplification: one limit set for every employee (see ShiftSettings doc).
/// </summary>
internal static class ShiftLimitValidator
{
    public static Result Validate(ShiftSettings settings, IReadOnlyList<ShiftAssignment> weekAssignments, Guid excludeAssignmentId, ShiftAssignment candidate)
    {
        var dailyLimit = candidate.IsNight && settings.NightWorkEnabled && settings.NightWorkMaxMinutes is { } nightLimit ? nightLimit : settings.MaxDailyMinutes;
        if (candidate.ShiftMinutes > dailyLimit)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDailyLimitExceeded);
        }

        var others = weekAssignments.Where(a => a.Id != excludeAssignmentId && a.Date != candidate.Date).ToList();
        if (others.Sum(a => a.ShiftMinutes) + candidate.ShiftMinutes > settings.MaxWeeklyMinutes)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftWeeklyHoursExceeded);
        }

        if (others.Select(a => a.Date).Distinct().Count() + 1 > settings.MaxWeeklyDays)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftWeeklyDaysExceeded);
        }

        return Result.Success();
    }
}

internal static class ShiftPlanLockGuard
{
    public static async Task<Result> EnsureNotLockedAsync(IShiftPlanLockRepository locks, DateOnly date, CancellationToken cancellationToken)
    {
        var period = await locks.FindByPeriodAsync(date, cancellationToken);
        return period is { IsLocked: true } ? Error.Conflict(AttendanceDomainErrors.ShiftPeriodLocked) : Result.Success();
    }
}

/// <summary>Haftalık/aylık vardiya ızgarası (11.3.6).</summary>
[RequiresPermission("attendance.shift.view")]
public sealed record ListShiftAssignmentsQuery(DateOnly From, DateOnly To, IReadOnlyList<Guid>? EmployeeIds) : IQuery<IReadOnlyList<ShiftAssignmentDto>>;

public sealed class ListShiftAssignmentsHandler(IShiftAssignmentRepository shifts) : IQueryHandler<ListShiftAssignmentsQuery, IReadOnlyList<ShiftAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<ShiftAssignmentDto>>> Handle(ListShiftAssignmentsQuery query, CancellationToken cancellationToken) =>
        (await shifts.ListForRangeAsync(query.From, query.To, query.EmployeeIds, cancellationToken)).Select(ShiftAssignmentMapper.ToDto).ToList();
}

/// <summary>Aylık görünüm "paylaşılmamış/bekleyen vardiyalar" sayacı.</summary>
[RequiresPermission("attendance.shift.view")]
public sealed record GetUnpublishedShiftCountQuery(DateOnly From, DateOnly To) : IQuery<int>;

public sealed class GetUnpublishedShiftCountHandler(IShiftAssignmentRepository shifts) : IQueryHandler<GetUnpublishedShiftCountQuery, int>
{
    public async Task<Result<int>> Handle(GetUnpublishedShiftCountQuery query, CancellationToken cancellationToken) =>
        await shifts.CountUnpublishedAsync(query.From, query.To, cancellationToken);
}

/// <summary>"Haftalık görünümde çalışana vardiya gir" - aynı çalışan+gün için upsert (kilitli dönemde reddedilir).</summary>
[RequiresPermission("attendance.shift.plan")]
public sealed record AssignShiftCommand(Guid EmployeeId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, bool IsNight, string? Note) : ICommand<Guid>;

public sealed class AssignShiftValidator : AbstractValidator<AssignShiftCommand>
{
    public AssignShiftValidator() => RuleFor(x => x.BreakMinutes).GreaterThanOrEqualTo(0);
}

public sealed class AssignShiftHandler(
    ITenantContext tenant, IShiftAssignmentRepository shifts, IShiftSettingsRepository settingsRepository, IShiftPlanLockRepository locks,
    IEmployeeLookup employeeLookup, IDataScopeService dataScope, IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<AssignShiftCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AssignShiftCommand command, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        if (!await employeeLookup.ExistsActiveAsync(tenant.TenantId, command.EmployeeId, cancellationToken))
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var lockCheck = await ShiftPlanLockGuard.EnsureNotLockedAsync(locks, command.Date, cancellationToken);
        if (lockCheck.IsFailure)
        {
            return lockCheck.Error;
        }

        var settings = await ShiftSettingsProvisioning.EnsureAsync(settingsRepository, tenant, unitOfWork, cancellationToken);
        var weekStart = TimesheetWeekEvaluator.MondayOf(command.Date);
        var weekAssignments = await shifts.ListForRangeAsync(weekStart, weekStart.AddDays(6), [command.EmployeeId], cancellationToken);

        var existing = await shifts.FindByEmployeeAndDateAsync(command.EmployeeId, command.Date, cancellationToken);
        if (existing is not null)
        {
            var candidateCheck = ShiftAssignment.Create(tenant.TenantId, command.EmployeeId, command.Date, command.Start, command.End, command.BreakMinutes, command.IsNight, command.Note);
            if (candidateCheck.IsFailure)
            {
                return candidateCheck.Error;
            }

            var limitCheck = ShiftLimitValidator.Validate(settings, weekAssignments, existing.Id, candidateCheck.Value);
            if (limitCheck.IsFailure)
            {
                return limitCheck.Error;
            }

            var updated = existing.Update(command.Start, command.End, command.BreakMinutes, command.IsNight, command.Note);
            return updated.IsFailure ? updated.Error : existing.Id;
        }

        var created = ShiftAssignment.Create(tenant.TenantId, command.EmployeeId, command.Date, command.Start, command.End, command.BreakMinutes, command.IsNight, command.Note);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var newLimitCheck = ShiftLimitValidator.Validate(settings, weekAssignments, Guid.Empty, created.Value);
        if (newLimitCheck.IsFailure)
        {
            return newLimitCheck.Error;
        }

        shifts.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>"Tekrarla": bir haftanın vardiyalarını seçilen çalışanlar için başka haftalara kopyalar.</summary>
[RequiresPermission("attendance.shift.plan")]
public sealed record RepeatShiftAssignmentsCommand(DateOnly SourceWeekStart, IReadOnlyList<DateOnly> TargetWeekStarts, IReadOnlyList<Guid> EmployeeIds) : ICommand;

public sealed class RepeatShiftAssignmentsValidator : AbstractValidator<RepeatShiftAssignmentsCommand>
{
    public RepeatShiftAssignmentsValidator()
    {
        RuleFor(x => x.TargetWeekStarts).NotEmpty();
        RuleFor(x => x.EmployeeIds).NotEmpty();
    }
}

public sealed class RepeatShiftAssignmentsHandler(
    ITenantContext tenant, IShiftAssignmentRepository shifts, IShiftSettingsRepository settingsRepository, IShiftPlanLockRepository locks,
    IDataScopeService dataScope, IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<RepeatShiftAssignmentsCommand>
{
    public async Task<Result> Handle(RepeatShiftAssignmentsCommand command, CancellationToken cancellationToken)
    {
        var sourceWeekStart = TimesheetWeekEvaluator.MondayOf(command.SourceWeekStart);
        var settings = await ShiftSettingsProvisioning.EnsureAsync(settingsRepository, tenant, unitOfWork, cancellationToken);

        foreach (var employeeId in command.EmployeeIds.Distinct())
        {
            if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, employeeId, cancellationToken))
            {
                return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
            }

            var sourceWeek = await shifts.ListForRangeAsync(sourceWeekStart, sourceWeekStart.AddDays(6), [employeeId], cancellationToken);
            foreach (var targetWeekStartRaw in command.TargetWeekStarts.Distinct())
            {
                var targetWeekStart = TimesheetWeekEvaluator.MondayOf(targetWeekStartRaw);
                var targetWeekAssignments = await shifts.ListForRangeAsync(targetWeekStart, targetWeekStart.AddDays(6), [employeeId], cancellationToken);

                foreach (var source in sourceWeek)
                {
                    var offset = source.Date.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)source.Date.DayOfWeek - 1;
                    var targetDate = targetWeekStart.AddDays(offset);

                    var lockCheck = await ShiftPlanLockGuard.EnsureNotLockedAsync(locks, targetDate, cancellationToken);
                    if (lockCheck.IsFailure)
                    {
                        return lockCheck.Error;
                    }

                    var existing = targetWeekAssignments.FirstOrDefault(a => a.Date == targetDate);
                    var candidateCheck = ShiftAssignment.Create(tenant.TenantId, employeeId, targetDate, source.Start, source.End, source.BreakMinutes, source.IsNight, source.Note);
                    if (candidateCheck.IsFailure)
                    {
                        return candidateCheck.Error;
                    }

                    var limitCheck = ShiftLimitValidator.Validate(settings, targetWeekAssignments, existing?.Id ?? Guid.Empty, candidateCheck.Value);
                    if (limitCheck.IsFailure)
                    {
                        return limitCheck.Error;
                    }

                    if (existing is not null)
                    {
                        var updated = existing.Update(source.Start, source.End, source.BreakMinutes, source.IsNight, source.Note);
                        if (updated.IsFailure)
                        {
                            return updated;
                        }
                    }
                    else
                    {
                        shifts.Add(candidateCheck.Value);
                        targetWeekAssignments = [.. targetWeekAssignments, candidateCheck.Value];
                    }
                }
            }
        }

        return Result.Success();
    }
}

/// <summary>"Yayınla": taslak vardiyalar çalışana bildirilir (Notifications entegrasyonu bu pakette yok, sonraki oturum).</summary>
[RequiresPermission("attendance.shift.publish")]
public sealed record PublishShiftAssignmentsCommand(IReadOnlyList<Guid> AssignmentIds) : ICommand;

public sealed class PublishShiftAssignmentsValidator : AbstractValidator<PublishShiftAssignmentsCommand>
{
    public PublishShiftAssignmentsValidator() => RuleFor(x => x.AssignmentIds).NotEmpty();
}

public sealed class PublishShiftAssignmentsHandler(IShiftAssignmentRepository shifts, IShiftPlanLockRepository locks, TimeProvider clock)
    : ICommandHandler<PublishShiftAssignmentsCommand>
{
    public async Task<Result> Handle(PublishShiftAssignmentsCommand command, CancellationToken cancellationToken)
    {
        foreach (var assignmentId in command.AssignmentIds.Distinct())
        {
            var found = await shifts.GetByIdAsync(assignmentId, cancellationToken);
            if (found.IsFailure)
            {
                return found.Error;
            }

            var lockCheck = await ShiftPlanLockGuard.EnsureNotLockedAsync(locks, found.Value.Date, cancellationToken);
            if (lockCheck.IsFailure)
            {
                return lockCheck.Error;
            }

            var published = found.Value.Publish(clock.GetUtcNow().UtcDateTime);
            if (published.IsFailure)
            {
                return published;
            }
        }

        return Result.Success();
    }
}
