using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.ShiftTemplates;

/// <summary>Bir vardiya tanımının plan satırına yazılacak saatleri (tanım tipine göre); gece vardiyasında bitiş başlangıçtan küçüktür.</summary>
internal static class ShiftTemplateTimes
{
    private const int MinutesPerDay = 24 * 60;

    public static (TimeOnly Start, TimeOnly End, int BreakMinutes) Resolve(ShiftDefinition definition)
    {
        var startMinute = definition.Type == ShiftType.Flexible ? definition.FlexStartEarliestMinute ?? definition.StartMinute : definition.StartMinute;
        var endMinute = definition.Type == ShiftType.Flexible && definition.DurationMinutes is { } duration ? startMinute + duration : definition.EndMinute;
        return (ToTime(startMinute), ToTime(endMinute), definition.Breaks.Sum(b => b.DurationMinutes));
    }

    private static TimeOnly ToTime(int minute) => TimeOnly.MinValue.AddMinutes(((minute % MinutesPerDay) + MinutesPerDay) % MinutesPerDay);
}

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
    private static readonly TimeOnly DayOffEnd = TimeOnly.MinValue.AddMinutes(1);

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
        var (start, end, breakMinutes) = dayType == ShiftDayType.Work && definition is not null
            ? ShiftTemplateTimes.Resolve(definition)
            : (TimeOnly.MinValue, DayOffEnd, 0);
        var isNight = dayType == ShiftDayType.Work && ShiftNightClassifier.Resolve(settings, start, end, false);

        var candidate = ShiftAssignment.Create(tenant.TenantId, employeeId, date, start, end, breakMinutes, isNight, null);
        if (candidate.IsFailure)
        {
            return candidate.Error;
        }

        if (dayType == ShiftDayType.Work)
        {
            var checks = await ValidateWorkAsync(employeeId, date, week, existing, candidate.Value, cancellationToken);
            if (checks.IsFailure)
            {
                return checks.Error;
            }
        }

        var target = existing ?? candidate.Value;
        if (existing is not null)
        {
            var updated = existing.Update(start, end, breakMinutes, isNight, null);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        var planned = target.SetPlan(dayType == ShiftDayType.Work ? definition?.Id : null, dayType);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        if (existing is null)
        {
            shifts.Add(target);
            week.Add(target);
            _added.Add(target);
        }

        return (target, existing is not null);
    }

    private async Task<Result> ValidateWorkAsync(
        Guid employeeId, DateOnly date, List<ShiftAssignment> week, ShiftAssignment? existing, ShiftAssignment candidate, CancellationToken cancellationToken)
    {
        // Yalnız çalışma günleri limit/dinlenme hesabına girer; gün türü olan satırların saati yoktur.
        var workWeek = week.Where(a => a.DayType == ShiftDayType.Work).ToList();
        var pending = _added.Where(a => a.DayType == ShiftDayType.Work).ToList();
        var known = await ShiftAdjacency.WithAdjacentDaysAsync(shifts, employeeId, date, workWeek, pending, cancellationToken);
        var limits = ShiftLimitValidator.Validate(settings, known.Where(a => a.DayType == ShiftDayType.Work).ToList(), existing?.Id ?? Guid.Empty, candidate);
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
