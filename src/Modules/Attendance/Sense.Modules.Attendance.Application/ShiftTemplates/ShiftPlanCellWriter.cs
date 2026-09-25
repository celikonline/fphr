using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.ShiftTemplates;

/// <summary>Ay başına tek sorgu ile plan kilidi denetimi (kilitli ay hücreleri atlanır/reddedilir).</summary>
internal sealed class ShiftLockCache(IShiftPlanLockRepository locks)
{
    private readonly Dictionary<DateOnly, bool> _months = [];

    public async Task<bool> IsLockedAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var month = ShiftPlanLock.NormalizeToMonthStart(date);
        if (!_months.TryGetValue(month, out var locked))
        {
            locked = (await ShiftPlanLockGuard.EnsureNotLockedAsync(locks, month, cancellationToken)).IsFailure;
            _months[month] = locked;
        }

        return locked;
    }
}

/// <summary>
/// Şablon uygulama ve Excel içe aktarma için ortak tek hücre yazıcısı: çalışma gününde vardiya tanımından saatleri türetir, yasal limit
/// (günlük/haftalık/dinlenme) ve "müsait değilim" çakışmasını denetler; çalışma günü olmayan türlerde saat tutmaz. Kaydetmez.
/// </summary>
internal sealed class ShiftPlanCellWriter(ITenantContext tenant, IShiftAssignmentRepository shifts, IEmployeeAvailabilityRepository availability, ShiftSettings settings)
{
    private readonly Dictionary<(Guid EmployeeId, DateOnly WeekStart), List<ShiftAssignment>> _weeks = [];
    private readonly List<ShiftAssignment> _added = [];

    public async Task<ShiftAssignment?> FindAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var week = await WeekAsync(employeeId, date, cancellationToken);
        return week.FirstOrDefault(a => a.Date == date);
    }

    /// <summary>Hücreyi yazar; dönen değer (atama, satır zaten var mıydı).</summary>
    public async Task<Result<(ShiftAssignment Assignment, bool Existed)>> WriteAsync(
        Guid employeeId, DateOnly date, ShiftDefinition? definition, ShiftDayType dayType, CancellationToken cancellationToken)
    {
        var week = await WeekAsync(employeeId, date, cancellationToken);
        var existing = week.FirstOrDefault(a => a.Date == date);
        var written = dayType == ShiftDayType.Work && definition is not null
            ? await WriteWorkAsync(employeeId, date, definition, week, existing, cancellationToken)
            : WriteDayOff(employeeId, date, dayType, existing);
        if (written.IsFailure)
        {
            return written.Error;
        }

        if (existing is null)
        {
            shifts.Add(written.Value);
            week.Add(written.Value);
            _added.Add(written.Value);
        }

        return (written.Value, existing is not null);
    }

    private Result<ShiftAssignment> WriteDayOff(Guid employeeId, DateOnly date, ShiftDayType dayType, ShiftAssignment? existing)
    {
        if (existing is null)
        {
            return ShiftAssignment.CreateDayOff(tenant.TenantId, employeeId, date, dayType);
        }

        var applied = existing.ApplyDayOff(dayType);
        return applied.IsFailure ? applied.Error : existing;
    }

    private async Task<Result<ShiftAssignment>> WriteWorkAsync(
        Guid employeeId, DateOnly date, ShiftDefinition definition, List<ShiftAssignment> week, ShiftAssignment? existing, CancellationToken cancellationToken)
    {
        var (start, end, breakMinutes) = ShiftDefinitionTimes.Resolve(definition);
        var isNight = ShiftNightClassifier.Resolve(settings, start, end, false);
        var candidate = ShiftAssignment.Create(tenant.TenantId, employeeId, date, start, end, breakMinutes, isNight, null);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        var checks = await ValidateWorkAsync(employeeId, date, week, existing, candidate.Value, cancellationToken);
        if (checks.IsFailure)
        {
            return checks.Error;
        }

        var target = existing ?? candidate.Value;
        if (existing is not null)
        {
            var updated = existing.Update(start, end, breakMinutes, isNight, existing.Note);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        var planned = target.SetPlan(definition.Id, ShiftDayType.Work);
        return planned.IsFailure ? planned.Error : target;
    }

    private async Task<Result> ValidateWorkAsync(
        Guid employeeId, DateOnly date, List<ShiftAssignment> week, ShiftAssignment? existing, ShiftAssignment candidate, CancellationToken cancellationToken)
    {
        // ShiftLimitValidator gün türü Work olmayan satırları kendisi saymaz.
        var known = await ShiftAdjacency.WithAdjacentDaysAsync(shifts, employeeId, date, week, _added, cancellationToken);
        var limits = ShiftLimitValidator.Validate(settings, known, existing?.Id ?? Guid.Empty, candidate);
        if (limits.IsFailure)
        {
            return limits;
        }

        return await ShiftAvailabilityGuard.EnsureNoConflictAsync(availability, employeeId, candidate, cancellationToken);
    }

    private async Task<List<ShiftAssignment>> WeekAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var weekStart = TimesheetWeekEvaluator.MondayOf(date);
        if (!_weeks.TryGetValue((employeeId, weekStart), out var week))
        {
            week = [.. await shifts.ListForRangeAsync(weekStart, weekStart.AddDays(6), [employeeId], cancellationToken)];
            _weeks[(employeeId, weekStart)] = week;
        }

        return week;
    }
}
