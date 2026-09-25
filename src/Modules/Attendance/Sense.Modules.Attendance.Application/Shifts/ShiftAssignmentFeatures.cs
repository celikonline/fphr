using FluentValidation;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Contracts.Events;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Shifts;

public sealed record ShiftAssignmentDto(
    Guid Id, Guid EmployeeId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, bool IsNight, string? Note,
    ShiftAssignmentStatus Status, DateTime? PublishedAt, int ShiftMinutes, Guid? ShiftDefinitionId = null, ShiftDayType DayType = ShiftDayType.Work);

internal static class ShiftAssignmentMapper
{
    public static ShiftAssignmentDto ToDto(ShiftAssignment a) =>
        new(a.Id, a.EmployeeId, a.Date, a.Start, a.End, a.BreakMinutes, a.IsNight, a.Note, a.Status, a.PublishedAt, a.ShiftMinutes, a.ShiftDefinitionId, a.DayType);
}

/// <summary>
/// Vardiya limitleri (11.3.6 "genel merkezin tanımladığı üst sınırların üzerine çıkamaz"): daily limit (night limit
/// when the shift is marked IsNight and a night limit is configured) + weekly hours/days across the ISO week
/// containing the candidate's date, then the minimum break (<see cref="ShiftSettings.BreakMinMinutes"/>, for shifts long
/// enough to require one) and the 11-hour uninterrupted rest against the neighbouring days' shifts.
/// <paramref name="knownAssignments"/> is the candidate's ISO week plus (when the candidate sits on the week's first/last
/// day) the adjacent day outside it - see <see cref="ShiftAdjacency"/>. MVP simplification: one limit set for every employee
/// (see ShiftSettings doc).
/// </summary>
internal static class ShiftLimitValidator
{
    public static Result Validate(ShiftSettings settings, IReadOnlyList<ShiftAssignment> knownAssignments, Guid excludeAssignmentId, ShiftAssignment candidate)
    {
        var dailyLimit = candidate.IsNight && settings.NightWorkEnabled && settings.NightWorkMaxMinutes is { } nightLimit ? nightLimit : settings.MaxDailyMinutes;
        if (candidate.ShiftMinutes > dailyLimit)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftDailyLimitExceeded);
        }

        var weekStart = TimesheetWeekEvaluator.MondayOf(candidate.Date);
        var weekEnd = weekStart.AddDays(6);
        var others = knownAssignments.Where(a => a.Id != excludeAssignmentId && a.Date != candidate.Date && a.Date >= weekStart && a.Date <= weekEnd).ToList();
        if (others.Sum(a => a.ShiftMinutes) + candidate.ShiftMinutes > settings.MaxWeeklyMinutes)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftWeeklyHoursExceeded);
        }

        if (others.Select(a => a.Date).Distinct().Count() + 1 > settings.MaxWeeklyDays)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftWeeklyDaysExceeded);
        }

        if (RequiresBreak(candidate) && candidate.BreakMinutes < settings.BreakMinMinutes)
        {
            return Error.Validation(AttendanceDomainErrors.ShiftBreakTooShort);
        }

        return HasEnoughRest(knownAssignments, excludeAssignmentId, candidate) ? Result.Success() : Error.Validation(AttendanceDomainErrors.ShiftRestPeriodTooShort);
    }

    /// <summary>The configured minimum break only binds shifts long enough for the m.68 ara dinlenme to apply at all (4 saatten kısa vardiyada mola şartı yok).</summary>
    private static bool RequiresBreak(ShiftAssignment candidate) =>
        DailyWorkCalculator.LegalMinimumBreakMinutes((int)ShiftSpan(candidate).TotalMinutes) > 0;

    private static bool HasEnoughRest(IReadOnlyList<ShiftAssignment> knownAssignments, Guid excludeAssignmentId, ShiftAssignment candidate)
    {
        var candidateStart = StartOf(candidate);
        var candidateEnd = EndOf(candidate);
        var minimumRest = TimeSpan.FromMinutes(ShiftSettings.MinDailyRestMinutes);
        foreach (var other in knownAssignments.Where(a => a.Id != excludeAssignmentId))
        {
            var isPrevious = other.Date == candidate.Date.AddDays(-1);
            var isNext = other.Date == candidate.Date.AddDays(1);
            if ((isPrevious && candidateStart - EndOf(other) < minimumRest) || (isNext && StartOf(other) - candidateEnd < minimumRest))
            {
                return false;
            }
        }

        return true;
    }

    private static TimeSpan ShiftSpan(ShiftAssignment a)
    {
        var span = a.End.ToTimeSpan() - a.Start.ToTimeSpan();
        return span <= TimeSpan.Zero ? span + TimeSpan.FromDays(1) : span;
    }

    private static DateTime StartOf(ShiftAssignment a) => a.Date.ToDateTime(a.Start);

    private static DateTime EndOf(ShiftAssignment a) => StartOf(a) + ShiftSpan(a);
}

/// <summary>Loads the neighbouring-day shifts the rest-period check needs when the candidate sits on the first/last day of its ISO week (inside the week they are already loaded).</summary>
internal static class ShiftAdjacency
{
    public static async Task<IReadOnlyList<ShiftAssignment>> WithAdjacentDaysAsync(
        IShiftAssignmentRepository shifts, Guid employeeId, DateOnly date, IReadOnlyList<ShiftAssignment> week, IReadOnlyList<ShiftAssignment> pending,
        CancellationToken cancellationToken)
    {
        var weekStart = TimesheetWeekEvaluator.MondayOf(date);
        var outside = new List<DateOnly>();
        if (date == weekStart)
        {
            outside.Add(date.AddDays(-1));
        }

        if (date == weekStart.AddDays(6))
        {
            outside.Add(date.AddDays(1));
        }

        if (outside.Count == 0)
        {
            return week;
        }

        var all = week.ToList();
        foreach (var adjacent in outside)
        {
            all.AddRange(await shifts.ListForRangeAsync(adjacent, adjacent, [employeeId], cancellationToken));
            all.AddRange(pending.Where(p => p.EmployeeId == employeeId && p.Date == adjacent));
        }

        return all.DistinctBy(a => a.Id).ToList();
    }
}

/// <summary>m.69: night-shift status is derived server-side from the shift hours; the client's flag can only add to it, never remove it.</summary>
internal static class ShiftNightClassifier
{
    public static bool Resolve(ShiftSettings settings, TimeOnly start, TimeOnly end, bool clientFlag) => clientFlag || settings.IsNightShift(start, end);
}

internal static class ShiftPlanLockGuard
{
    public static async Task<Result> EnsureNotLockedAsync(IShiftPlanLockRepository locks, DateOnly date, CancellationToken cancellationToken)
    {
        var period = await locks.FindByPeriodAsync(date, cancellationToken);
        return period is { IsLocked: true } ? Error.Conflict(AttendanceDomainErrors.ShiftPeriodLocked) : Result.Success();
    }
}

/// <summary>"Müsait değilim" çakışması (package A5): a planner cannot assign a shift over an active availability entry that overlaps it.</summary>
internal static class ShiftAvailabilityGuard
{
    public static async Task<Result> EnsureNoConflictAsync(IEmployeeAvailabilityRepository availability, Guid employeeId, ShiftAssignment candidate, CancellationToken cancellationToken)
    {
        var entries = await availability.ListForEmployeeAsync(employeeId, candidate.Date, candidate.Date, cancellationToken);
        return entries.Any(e => e.ConflictsWith(candidate.Start, candidate.End))
            ? Error.Conflict(AttendanceDomainErrors.ShiftAvailabilityConflict)
            : Result.Success();
    }
}

/// <summary>Haftalık/aylık vardiya ızgarası (11.3.6).</summary>
[RequiresPermission("attendance.shift.view")]
public sealed record ListShiftAssignmentsQuery(DateOnly From, DateOnly To, IReadOnlyList<Guid>? EmployeeIds) : IQuery<IReadOnlyList<ShiftAssignmentDto>>;

public sealed class ListShiftAssignmentsHandler(IShiftAssignmentRepository shifts, IDataScopeService dataScope)
    : IQueryHandler<ListShiftAssignmentsQuery, IReadOnlyList<ShiftAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<ShiftAssignmentDto>>> Handle(ListShiftAssignmentsQuery query, CancellationToken cancellationToken)
    {
        // Grid rows are narrowed to the caller's data scope (null = unrestricted). An empty/omitted employeeIds filter means "everyone the caller
        // may see", never "everyone"; ids outside the scope are silently dropped so a stale client filter cannot widen the result.
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, cancellationToken);
        IReadOnlyCollection<Guid>? employeeIds = query.EmployeeIds is { Count: > 0 } ? query.EmployeeIds : null;
        if (visible is not null)
        {
            employeeIds = (employeeIds ?? visible).Where(visible.Contains).Distinct().ToList();
            if (employeeIds.Count == 0)
            {
                return Result.Success<IReadOnlyList<ShiftAssignmentDto>>([]);
            }
        }

        return (await shifts.ListForRangeAsync(query.From, query.To, employeeIds, cancellationToken)).Select(ShiftAssignmentMapper.ToDto).ToList();
    }
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
    IEmployeeAvailabilityRepository availability, IEmployeeLookup employeeLookup, IDataScopeService dataScope, IAttendanceUnitOfWork unitOfWork)
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
        var knownAssignments = await ShiftAdjacency.WithAdjacentDaysAsync(shifts, command.EmployeeId, command.Date, weekAssignments, [], cancellationToken);
        var isNight = ShiftNightClassifier.Resolve(settings, command.Start, command.End, command.IsNight);

        var existing = await shifts.FindByEmployeeAndDateAsync(command.EmployeeId, command.Date, cancellationToken);
        if (existing is not null)
        {
            var candidateCheck = ShiftAssignment.Create(tenant.TenantId, command.EmployeeId, command.Date, command.Start, command.End, command.BreakMinutes, isNight, command.Note);
            if (candidateCheck.IsFailure)
            {
                return candidateCheck.Error;
            }

            var limitCheck = ShiftLimitValidator.Validate(settings, knownAssignments, existing.Id, candidateCheck.Value);
            if (limitCheck.IsFailure)
            {
                return limitCheck.Error;
            }

            var availabilityCheck = await ShiftAvailabilityGuard.EnsureNoConflictAsync(availability, command.EmployeeId, candidateCheck.Value, cancellationToken);
            if (availabilityCheck.IsFailure)
            {
                return availabilityCheck.Error;
            }

            var updated = existing.Update(command.Start, command.End, command.BreakMinutes, isNight, command.Note);
            return updated.IsFailure ? updated.Error : existing.Id;
        }

        var created = ShiftAssignment.Create(tenant.TenantId, command.EmployeeId, command.Date, command.Start, command.End, command.BreakMinutes, isNight, command.Note);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var newLimitCheck = ShiftLimitValidator.Validate(settings, knownAssignments, Guid.Empty, created.Value);
        if (newLimitCheck.IsFailure)
        {
            return newLimitCheck.Error;
        }

        var newAvailabilityCheck = await ShiftAvailabilityGuard.EnsureNoConflictAsync(availability, command.EmployeeId, created.Value, cancellationToken);
        if (newAvailabilityCheck.IsFailure)
        {
            return newAvailabilityCheck.Error;
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
    IEmployeeAvailabilityRepository availability, IDataScopeService dataScope, IAttendanceUnitOfWork unitOfWork)
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

            var pending = new List<ShiftAssignment>();
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
                    var isNight = ShiftNightClassifier.Resolve(settings, source.Start, source.End, source.IsNight);
                    var candidateCheck = ShiftAssignment.Create(tenant.TenantId, employeeId, targetDate, source.Start, source.End, source.BreakMinutes, isNight, source.Note);
                    if (candidateCheck.IsFailure)
                    {
                        return candidateCheck.Error;
                    }

                    var knownAssignments = await ShiftAdjacency.WithAdjacentDaysAsync(shifts, employeeId, targetDate, targetWeekAssignments, pending, cancellationToken);
                    var limitCheck = ShiftLimitValidator.Validate(settings, knownAssignments, existing?.Id ?? Guid.Empty, candidateCheck.Value);
                    if (limitCheck.IsFailure)
                    {
                        return limitCheck.Error;
                    }

                    var availabilityCheck = await ShiftAvailabilityGuard.EnsureNoConflictAsync(availability, employeeId, candidateCheck.Value, cancellationToken);
                    if (availabilityCheck.IsFailure)
                    {
                        return availabilityCheck.Error;
                    }

                    if (existing is not null)
                    {
                        var updated = existing.Update(source.Start, source.End, source.BreakMinutes, isNight, source.Note);
                        if (updated.IsFailure)
                        {
                            return updated;
                        }
                    }
                    else
                    {
                        shifts.Add(candidateCheck.Value);
                        targetWeekAssignments = [.. targetWeekAssignments, candidateCheck.Value];
                        pending.Add(candidateCheck.Value);
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

public sealed class PublishShiftAssignmentsHandler(
    IShiftAssignmentRepository shifts, IShiftPlanLockRepository locks, TimeProvider clock,
    ITenantContext tenant, IEmployeeLookup employeeLookup, IIntegrationEventOutbox outbox)
    : ICommandHandler<PublishShiftAssignmentsCommand>
{
    public async Task<Result> Handle(PublishShiftAssignmentsCommand command, CancellationToken cancellationToken)
    {
        var published = new List<ShiftAssignment>();
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

            var publishResult = found.Value.Publish(clock.GetUtcNow().UtcDateTime);
            if (publishResult.IsFailure)
            {
                return publishResult;
            }

            published.Add(found.Value);
        }

        await RaiseShiftPlanPublishedAsync(published, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Groups the just-published assignments by (employee's org unit, week) and raises one <see cref="ShiftPlanPublished"/>
    /// event per group - a single publish call can span several weeks/units. Employees with no org unit are skipped
    /// (the event has no "unassigned" bucket).
    /// </summary>
    private async Task RaiseShiftPlanPublishedAsync(IReadOnlyList<ShiftAssignment> published, CancellationToken cancellationToken)
    {
        if (published.Count == 0)
        {
            return;
        }

        var orgUnitCache = new Dictionary<Guid, Guid?>();
        var groups = new Dictionary<(Guid OrgUnitId, DateOnly WeekStart), HashSet<Guid>>();
        foreach (var assignment in published)
        {
            if (!orgUnitCache.TryGetValue(assignment.EmployeeId, out var orgUnitId))
            {
                orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, assignment.EmployeeId, cancellationToken);
                orgUnitCache[assignment.EmployeeId] = orgUnitId;
            }

            if (orgUnitId is null)
            {
                continue;
            }

            var key = (orgUnitId.Value, TimesheetWeekEvaluator.MondayOf(assignment.Date));
            if (!groups.TryGetValue(key, out var employeeIds))
            {
                employeeIds = [];
                groups[key] = employeeIds;
            }

            employeeIds.Add(assignment.EmployeeId);
        }

        foreach (var ((orgUnitId, weekStart), employeeIds) in groups)
        {
            outbox.Enqueue(new ShiftPlanPublished(tenant.TenantId, orgUnitId, weekStart, employeeIds.ToList()));
        }
    }
}

/// <summary>"Vardiyam" (package A5 self-servis).</summary>
[RequiresPermission("attendance.shift.self")]
public sealed record GetMyShiftAssignmentsQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<ShiftAssignmentDto>>;

public sealed class GetMyShiftAssignmentsHandler(IShiftAssignmentRepository shifts, ICurrentUser currentUser)
    : IQueryHandler<GetMyShiftAssignmentsQuery, IReadOnlyList<ShiftAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<ShiftAssignmentDto>>> Handle(GetMyShiftAssignmentsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        // Drafts are planner work-in-progress; the employee only sees a shift once it has been published (Yayınla notifies them).
        var list = await shifts.ListForRangeAsync(query.From, query.To, [employeeId], cancellationToken);
        return list.Where(a => a.Status == ShiftAssignmentStatus.Published).Select(ShiftAssignmentMapper.ToDto).ToList();
    }
}

/// <summary>Toplu işlemde atlanan hücrenin nedeni (web: <c>locked</c> | <c>published</c>).</summary>
public enum ShiftBulkSkipReason
{
    Locked = 0,
    Published = 1,
}

public sealed record ShiftBulkSkippedDto(Guid EmployeeId, DateOnly Date, ShiftBulkSkipReason Reason);

public sealed record ShiftBulkResultDto(int AffectedCount, IReadOnlyList<ShiftBulkSkippedDto> Skipped);

/// <summary>Toplu atama/silme için tek kartezyen çarpım (çalışan x gün) ve hücre bazlı kilit/yayın denetimi.</summary>
internal static class ShiftBulkCells
{
    public const int MaxCells = 5000;

    /// <summary>Hücrenin ayı (dönem) kilitliyse true; ay başına tek sorgu (hafta iki aya yayılınca her hücre kendi ayına göre değerlendirilir).</summary>
    public static async Task<bool> IsLockedAsync(IShiftPlanLockRepository locks, Dictionary<DateOnly, bool> cache, DateOnly date, CancellationToken cancellationToken)
    {
        var month = ShiftPlanLock.NormalizeToMonthStart(date);
        if (!cache.TryGetValue(month, out var isLocked))
        {
            isLocked = (await ShiftPlanLockGuard.EnsureNotLockedAsync(locks, month, cancellationToken)).IsFailure;
            cache[month] = isLocked;
        }

        return isLocked;
    }

    public static async Task<Result> EnsureInScopeAndActiveAsync(
        IReadOnlyCollection<Guid> employeeIds, Guid tenantId, IDataScopeService dataScope, IEmployeeLookup employeeLookup, CancellationToken cancellationToken)
    {
        foreach (var employeeId in employeeIds)
        {
            if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.ShiftPlan.Key, employeeId, cancellationToken))
            {
                return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
            }

            if (!await employeeLookup.ExistsActiveAsync(tenantId, employeeId, cancellationToken))
            {
                return Error.NotFound(AttendanceErrors.EmployeeNotFound);
            }
        }

        return Result.Success();
    }

    /// <summary>Kilit/limit/yakın gün (dinlenme) kontrolleri için hafta+komşu günler; hepsi tek sorguda yüklenir.</summary>
    public static async Task<Dictionary<Guid, List<ShiftAssignment>>> LoadAsync(
        IShiftAssignmentRepository shifts, IReadOnlyCollection<Guid> employeeIds, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken)
    {
        var from = TimesheetWeekEvaluator.MondayOf(dates.Min()).AddDays(-1);
        var to = TimesheetWeekEvaluator.MondayOf(dates.Max()).AddDays(7);
        var loaded = await shifts.ListForRangeAsync(from, to, employeeIds, cancellationToken);
        var byEmployee = employeeIds.ToDictionary(id => id, _ => new List<ShiftAssignment>());
        foreach (var assignment in loaded)
        {
            if (byEmployee.TryGetValue(assignment.EmployeeId, out var list))
            {
                list.Add(assignment);
            }
        }

        return byEmployee;
    }
}

/// <summary>Seçilen çalışan x gün hücrelerine vardiya tanımı ya da gün türü (hafta tatili/dinlenme/resmi tatil) atar.</summary>
[RequiresPermission("attendance.shift.plan")]
public sealed record BulkAssignShiftsCommand(
    IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<DateOnly> Dates, Guid? ShiftDefinitionId, ShiftDayType DayType, bool OverwritePublished)
    : ICommand<ShiftBulkResultDto>;

public sealed class BulkAssignShiftsValidator : AbstractValidator<BulkAssignShiftsCommand>
{
    public BulkAssignShiftsValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.Dates).NotEmpty();
        RuleFor(x => x.DayType).IsInEnum();
        RuleFor(x => x).Must(x => x.EmployeeIds.Distinct().Count() * x.Dates.Distinct().Count() <= ShiftBulkCells.MaxCells)
            .When(x => x.EmployeeIds is { Count: > 0 } && x.Dates is { Count: > 0 });
    }
}

public sealed class BulkAssignShiftsHandler(
    ITenantContext tenant, IShiftAssignmentRepository shifts, IShiftDefinitionRepository definitions, IShiftSettingsRepository settingsRepository,
    IShiftPlanLockRepository locks, IEmployeeAvailabilityRepository availability, IEmployeeLookup employeeLookup, IDataScopeService dataScope,
    IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<BulkAssignShiftsCommand, ShiftBulkResultDto>
{
    public async Task<Result<ShiftBulkResultDto>> Handle(BulkAssignShiftsCommand command, CancellationToken cancellationToken)
    {
        var employeeIds = command.EmployeeIds.Distinct().ToList();
        var dates = command.Dates.Distinct().Order().ToList();

        var access = await ShiftBulkCells.EnsureInScopeAndActiveAsync(employeeIds, tenant.TenantId, dataScope, employeeLookup, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var isWork = command.DayType == ShiftDayType.Work;
        ShiftDefinition? definition = null;
        if (isWork)
        {
            if (command.ShiftDefinitionId is not { } definitionId)
            {
                return Error.Validation(AttendanceDomainErrors.ShiftDefinitionRequired);
            }

            definition = (await definitions.ListByIdsAsync([definitionId], cancellationToken)).FirstOrDefault(d => d.IsActive);
            if (definition is null)
            {
                return Error.NotFound(AttendanceDomainErrors.ShiftDefinitionNotFound);
            }
        }

        var settings = isWork ? await ShiftSettingsProvisioning.EnsureAsync(settingsRepository, tenant, unitOfWork, cancellationToken) : null;
        var known = await ShiftBulkCells.LoadAsync(shifts, employeeIds, dates, cancellationToken);
        var lockCache = new Dictionary<DateOnly, bool>();
        var skipped = new List<ShiftBulkSkippedDto>();
        var affected = 0;

        foreach (var employeeId in employeeIds)
        {
            var employeeShifts = known[employeeId];
            foreach (var date in dates)
            {
                if (await ShiftBulkCells.IsLockedAsync(locks, lockCache, date, cancellationToken))
                {
                    skipped.Add(new ShiftBulkSkippedDto(employeeId, date, ShiftBulkSkipReason.Locked));
                    continue;
                }

                var existing = employeeShifts.FirstOrDefault(a => a.Date == date);
                if (existing is { Status: ShiftAssignmentStatus.Published } && !command.OverwritePublished)
                {
                    skipped.Add(new ShiftBulkSkippedDto(employeeId, date, ShiftBulkSkipReason.Published));
                    continue;
                }

                var applied = isWork
                    ? await ApplyWorkAsync(employeeId, date, existing, employeeShifts, definition!, settings!, cancellationToken)
                    : ApplyDayOff(employeeId, date, existing, employeeShifts, command.DayType);
                if (applied.IsFailure)
                {
                    return applied.Error;
                }

                affected++;
            }
        }

        return new ShiftBulkResultDto(affected, skipped);
    }

    private Result ApplyDayOff(Guid employeeId, DateOnly date, ShiftAssignment? existing, List<ShiftAssignment> employeeShifts, ShiftDayType dayType)
    {
        if (existing is not null)
        {
            return existing.ApplyDayOff(dayType);
        }

        var created = ShiftAssignment.CreateDayOff(tenant.TenantId, employeeId, date, dayType);
        if (created.IsFailure)
        {
            return created;
        }

        shifts.Add(created.Value);
        employeeShifts.Add(created.Value);
        return Result.Success();
    }

    private async Task<Result> ApplyWorkAsync(
        Guid employeeId, DateOnly date, ShiftAssignment? existing, List<ShiftAssignment> employeeShifts, ShiftDefinition definition, ShiftSettings settings,
        CancellationToken cancellationToken)
    {
        var (start, end, breakMinutes) = ShiftDefinitionTimes.Resolve(definition);
        var isNight = ShiftNightClassifier.Resolve(settings, start, end, false);
        var candidate = ShiftAssignment.Create(tenant.TenantId, employeeId, date, start, end, breakMinutes, isNight, null);
        if (candidate.IsFailure)
        {
            return candidate;
        }

        // Yalnız çalışma günleri limit/dinlenme hesabına girer; gün türü olan satırların saati yoktur.
        var workDays = employeeShifts.Where(a => a.DayType == ShiftDayType.Work).ToList();
        var limitCheck = ShiftLimitValidator.Validate(settings, workDays, existing?.Id ?? Guid.Empty, candidate.Value);
        if (limitCheck.IsFailure)
        {
            return limitCheck;
        }

        var availabilityCheck = await ShiftAvailabilityGuard.EnsureNoConflictAsync(availability, employeeId, candidate.Value, cancellationToken);
        if (availabilityCheck.IsFailure)
        {
            return availabilityCheck;
        }

        if (existing is not null)
        {
            var updated = existing.Update(start, end, breakMinutes, isNight, existing.Note);
            return updated.IsFailure ? updated : existing.SetPlan(definition.Id, ShiftDayType.Work);
        }

        var plan = candidate.Value.SetPlan(definition.Id, ShiftDayType.Work);
        if (plan.IsFailure)
        {
            return plan;
        }

        shifts.Add(candidate.Value);
        employeeShifts.Add(candidate.Value);
        return Result.Success();
    }
}

/// <summary>Vardiya tanımının atama satırına yansıyan saatleri: dakikalar gün başından; esnek vardiyada bitiş = başlangıç + sabit süre; mola = tanımdaki mola toplamı.</summary>
internal static class ShiftDefinitionTimes
{
    public static (TimeOnly Start, TimeOnly End, int BreakMinutes) Resolve(ShiftDefinition definition)
    {
        var endMinute = definition is { Type: ShiftType.Flexible, DurationMinutes: { } duration }
            ? (definition.StartMinute + duration) % AttendanceLimits.MaxMinutesPerDay
            : definition.EndMinute;
        return (TimeOnly.MinValue.AddMinutes(definition.StartMinute), TimeOnly.MinValue.AddMinutes(endMinute), definition.Breaks.Sum(b => b.DurationMinutes));
    }
}

/// <summary>Seçilen hücrelerdeki atamaları siler (kilitli dönem hücreleri ve yayınlanmışlar - bayrak yoksa - atlanıp bildirilir).</summary>
[RequiresPermission("attendance.shift.plan")]
public sealed record BulkClearShiftsCommand(IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<DateOnly> Dates, bool OverwritePublished) : ICommand<ShiftBulkResultDto>;

public sealed class BulkClearShiftsValidator : AbstractValidator<BulkClearShiftsCommand>
{
    public BulkClearShiftsValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.Dates).NotEmpty();
        RuleFor(x => x).Must(x => x.EmployeeIds.Distinct().Count() * x.Dates.Distinct().Count() <= ShiftBulkCells.MaxCells)
            .When(x => x.EmployeeIds is { Count: > 0 } && x.Dates is { Count: > 0 });
    }
}

public sealed class BulkClearShiftsHandler(
    ITenantContext tenant, IShiftAssignmentRepository shifts, IShiftPlanLockRepository locks, IEmployeeLookup employeeLookup, IDataScopeService dataScope)
    : ICommandHandler<BulkClearShiftsCommand, ShiftBulkResultDto>
{
    public async Task<Result<ShiftBulkResultDto>> Handle(BulkClearShiftsCommand command, CancellationToken cancellationToken)
    {
        var employeeIds = command.EmployeeIds.Distinct().ToList();
        var dates = command.Dates.Distinct().Order().ToList();

        var access = await ShiftBulkCells.EnsureInScopeAndActiveAsync(employeeIds, tenant.TenantId, dataScope, employeeLookup, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var known = await ShiftBulkCells.LoadAsync(shifts, employeeIds, dates, cancellationToken);
        var lockCache = new Dictionary<DateOnly, bool>();
        var skipped = new List<ShiftBulkSkippedDto>();
        var affected = 0;

        foreach (var employeeId in employeeIds)
        {
            foreach (var date in dates)
            {
                var existing = known[employeeId].FirstOrDefault(a => a.Date == date);
                if (existing is null)
                {
                    continue;
                }

                if (await ShiftBulkCells.IsLockedAsync(locks, lockCache, date, cancellationToken))
                {
                    skipped.Add(new ShiftBulkSkippedDto(employeeId, date, ShiftBulkSkipReason.Locked));
                    continue;
                }

                if (existing.Status == ShiftAssignmentStatus.Published && !command.OverwritePublished)
                {
                    skipped.Add(new ShiftBulkSkippedDto(employeeId, date, ShiftBulkSkipReason.Published));
                    continue;
                }

                shifts.Remove(existing);
                affected++;
            }
        }

        return new ShiftBulkResultDto(affected, skipped);
    }
}

/// <summary>Aralıkta HİÇ ataması (gün türü dahil) olmayan çalışanlar; employeeIds boşsa kapsamdaki görünür çalışanlar (kapsam sınırsızsa boş döner - çalışan listesi bu modülde yok).</summary>
[RequiresPermission("attendance.shift.view")]
public sealed record ListUnassignedEmployeesQuery(DateOnly From, DateOnly To, IReadOnlyList<Guid>? EmployeeIds) : IQuery<IReadOnlyList<Guid>>;

public sealed class ListUnassignedEmployeesHandler(IShiftAssignmentRepository shifts, IDataScopeService dataScope)
    : IQueryHandler<ListUnassignedEmployeesQuery, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(ListUnassignedEmployeesQuery query, CancellationToken cancellationToken)
    {
        var candidates = await ShiftPlanScope.ResolveAsync(dataScope, query.EmployeeIds, cancellationToken);
        if (candidates is null || candidates.Count == 0)
        {
            return Result.Success<IReadOnlyList<Guid>>([]);
        }

        var assigned = (await shifts.ListForRangeAsync(query.From, query.To, candidates, cancellationToken)).Select(a => a.EmployeeId).ToHashSet();
        return candidates.Where(id => !assigned.Contains(id)).ToList();
    }
}

internal static class ShiftPlanScope
{
    /// <summary>İstemci filtresi ile veri kapsamının kesişimi; ikisi de yoksa null (belirsiz küme).</summary>
    public static async Task<IReadOnlyList<Guid>?> ResolveAsync(IDataScopeService dataScope, IReadOnlyList<Guid>? requested, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, cancellationToken);
        IReadOnlyCollection<Guid>? ids = requested is { Count: > 0 } ? requested : visible;
        if (ids is null)
        {
            return null;
        }

        return (visible is null ? ids : ids.Where(visible.Contains)).Distinct().ToList();
    }
}

public sealed record ShiftDayTotalDto(DateOnly Date, int EmployeeCount, int TotalMinutes);

public sealed record ShiftEmployeeTotalDto(Guid EmployeeId, int TotalMinutes);

public sealed record ShiftPlanTotalsDto(IReadOnlyList<ShiftDayTotalDto> ByDate, IReadOnlyList<ShiftEmployeeTotalDto> ByEmployee);

/// <summary>Takvim dip toplamı: gün ve çalışan bazında planlanan dakika; gün türü Work olmayan günler 0 dakika sayılır ve çalışan sayısına girmez.</summary>
[RequiresPermission("attendance.shift.view")]
public sealed record GetShiftPlanTotalsQuery(DateOnly From, DateOnly To, IReadOnlyList<Guid>? EmployeeIds) : IQuery<ShiftPlanTotalsDto>;

public sealed class GetShiftPlanTotalsHandler(IShiftAssignmentRepository shifts, IDataScopeService dataScope)
    : IQueryHandler<GetShiftPlanTotalsQuery, ShiftPlanTotalsDto>
{
    public async Task<Result<ShiftPlanTotalsDto>> Handle(GetShiftPlanTotalsQuery query, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, cancellationToken);
        var scoped = query.EmployeeIds is { Count: > 0 } ? query.EmployeeIds : null;
        if (visible is not null)
        {
            scoped = (scoped ?? visible.ToList()).Where(visible.Contains).Distinct().ToList();
            if (scoped.Count == 0)
            {
                return new ShiftPlanTotalsDto([], []);
            }
        }

        var work = (await shifts.ListForRangeAsync(query.From, query.To, scoped, cancellationToken)).Where(a => a.DayType == ShiftDayType.Work).ToList();
        var byDate = work.GroupBy(a => a.Date).OrderBy(g => g.Key)
            .Select(g => new ShiftDayTotalDto(g.Key, g.Select(a => a.EmployeeId).Distinct().Count(), g.Sum(a => a.ShiftMinutes))).ToList();
        var byEmployee = work.GroupBy(a => a.EmployeeId).Select(g => new ShiftEmployeeTotalDto(g.Key, g.Sum(a => a.ShiftMinutes))).ToList();
        return new ShiftPlanTotalsDto(byDate, byEmployee);
    }
}
