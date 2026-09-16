using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Leave.Application;

/// <summary>Leave DbContext's unit of work, for integration event handlers that run outside the command pipeline.</summary>
public interface ILeaveUnitOfWork : IUnitOfWork;

/// <summary>
/// Leave's read port for the employee's work schedule. Roadmap #23 is done: the Infrastructure implementation
/// (<c>WorkScheduleReaderAdapter</c>) adapts Attendance.Contracts.IWorkScheduleReadService, so this reflects the
/// employee's actual assigned schedule rather than a Monday–Friday assumption.
/// </summary>
public interface IWorkScheduleReader
{
    Task<Func<DateOnly, bool>> GetWorkingDayPredicateAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Planned working intervals (local wall-clock) on the days [from, to], holidays not yet removed.</summary>
    Task<IReadOnlyList<ScheduledInterval>> GetScheduledIntervalsAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Pazartesi'den Cuma'ya izin alırsa Cumartesi'yi ekle" (roadmap #23): whether the schedule in effect on
    /// <paramref name="date"/> (typically a Saturday) has the flag on. Resolved per date, like every other schedule
    /// read, so a schedule reassignment mid-range is honored at its EffectiveFrom.
    /// </summary>
    Task<Func<DateOnly, bool>> GetIncludeSaturdayWhenFullWeekPredicateAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>Leave day calculation for a date range (work schedule + the tenant's effective holiday calendar).</summary>
public interface ILeaveDayCalculator
{
    Task<LeaveDayCount> CalculateAsync(
        Guid tenantId, Guid employeeId, DateOnly start, DateOnly end, bool countsNonWorkingDays, CancellationToken cancellationToken = default);
}

public sealed class LeaveDayCalculator(IWorkScheduleReader schedules, IHolidayCalendarLookup holidays) : ILeaveDayCalculator
{
    public async Task<LeaveDayCount> CalculateAsync(
        Guid tenantId, Guid employeeId, DateOnly start, DateOnly end, bool countsNonWorkingDays, CancellationToken cancellationToken = default)
    {
        // The Saturday rule (roadmap #23) can add a Saturday up to 6 days past `end` (a Mon–Fri request's own week);
        // both predicates are fetched over that wider window so LeaveDayCounter never sees an unresolved date.
        var scheduleWindowEnd = end.AddDays(6);
        var isWorkingDay = await schedules.GetWorkingDayPredicateAsync(tenantId, employeeId, start, scheduleWindowEnd, cancellationToken);
        var includeSaturdayWhenFullWeek = await schedules.GetIncludeSaturdayWhenFullWeekPredicateAsync(tenantId, employeeId, start, scheduleWindowEnd, cancellationToken);
        var calendar = await holidays.ResolveCalendarAsync(tenantId, null, null, cancellationToken);
        var holidayMap = calendar is null
            ? new Dictionary<DateOnly, string>()
            : (await holidays.GetHolidaysAsync(tenantId, calendar.Id, start, end, cancellationToken))
                .GroupBy(h => h.Date)
                .ToDictionary(g => g.Key, g => g.First().Name);

        return LeaveDayCounter.Count(start, end, isWorkingDay, holidayMap, countsNonWorkingDays, includeSaturdayWhenFullWeek);
    }
}

/// <summary>Builds the Domain's statutory value records from LegislationParameter (fallback: 2026 TR values).</summary>
public static class StatutoryLeaveResolver
{
    private static readonly string[] Keys =
    [
        LegislationParameterKeys.AnnualTier1Days, LegislationParameterKeys.AnnualTier2AfterYears, LegislationParameterKeys.AnnualTier2Days,
        LegislationParameterKeys.AnnualTier3AfterYears, LegislationParameterKeys.AnnualTier3Days, LegislationParameterKeys.AnnualYoungAgeMax,
        LegislationParameterKeys.AnnualOldAgeMin, LegislationParameterKeys.AnnualAgeExceptionMinDays, LegislationParameterKeys.PaternityDays,
        LegislationParameterKeys.MaternityPreBirthWeeks, LegislationParameterKeys.MaternityPostBirthWeeks, LegislationParameterKeys.MarriageDays,
        LegislationParameterKeys.BereavementDays, LegislationParameterKeys.FosterCareDays, LegislationParameterKeys.PaidMilitaryServiceDays,
    ];

    public static async Task<StatutoryLeaveDurations> ResolveAsync(ILegislationParameterReader reader, DateOnly asOf, CancellationToken cancellationToken)
    {
        var values = await reader.ListAsync(Keys, LegislationCountries.Turkey, cancellationToken);
        return Resolve(values, asOf);
    }

    public static StatutoryLeaveDurations Resolve(IReadOnlyList<LegislationParameterValue> values, DateOnly asOf)
    {
        var fallback = StatutoryLeaveDurations.Turkey2026;
        decimal D(string key, decimal def) => values.Resolve(key, asOf)?.DecimalValue ?? def;
        var a = fallback.Annual;

        return new StatutoryLeaveDurations(
            new StatutoryAnnualLeave(
                D(LegislationParameterKeys.AnnualTier1Days, a.Tier1Days),
                (int)D(LegislationParameterKeys.AnnualTier2AfterYears, a.Tier2AfterYears),
                D(LegislationParameterKeys.AnnualTier2Days, a.Tier2Days),
                (int)D(LegislationParameterKeys.AnnualTier3AfterYears, a.Tier3AfterYears),
                D(LegislationParameterKeys.AnnualTier3Days, a.Tier3Days),
                (int)D(LegislationParameterKeys.AnnualYoungAgeMax, a.YoungAgeMax),
                (int)D(LegislationParameterKeys.AnnualOldAgeMin, a.OldAgeMin),
                D(LegislationParameterKeys.AnnualAgeExceptionMinDays, a.AgeExceptionMinDays)),
            D(LegislationParameterKeys.PaternityDays, fallback.PaternityDays),
            D(LegislationParameterKeys.MaternityPreBirthWeeks, fallback.MaternityPreBirthWeeks),
            D(LegislationParameterKeys.MaternityPostBirthWeeks, fallback.MaternityPostBirthWeeks),
            D(LegislationParameterKeys.MarriageDays, fallback.MarriageDays),
            D(LegislationParameterKeys.BereavementDays, fallback.BereavementDays),
            D(LegislationParameterKeys.FosterCareDays, fallback.FosterCareDays),
            D(LegislationParameterKeys.PaidMilitaryServiceDays, fallback.PaidMilitaryServiceDays));
    }
}
