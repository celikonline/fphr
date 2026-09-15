using Sense.Modules.Leave.Domain.Balances;

namespace Sense.Modules.Leave.Domain.RuleSets;

/// <summary>Statutory durations resolved from LegislationParameter on the seeding date.</summary>
public sealed record StatutoryLeaveDurations(
    StatutoryAnnualLeave Annual,
    decimal PaternityDays,
    decimal MaternityPreBirthWeeks,
    decimal MaternityPostBirthWeeks,
    decimal MarriageDays,
    decimal BereavementDays,
    decimal FosterCareDays,
    decimal PaidMilitaryServiceDays)
{
    public static StatutoryLeaveDurations Turkey2026 { get; } = new(StatutoryAnnualLeave.Turkey2026, 10m, 8m, 16m, 3m, 3m, 10m, 21m);
}

/// <summary>"Örnek Türk İş Kanunu kuralı" - the default rule set every tenant starts with (11.3.5 seed list, seed version 2026.05).</summary>
public static class LeaveRuleSetSeed
{
    public const string DefaultRuleSetName = "leave.rule_set.seed.turkish_labour_law";
    private const int DaysPerWeek = 7;
    private const decimal RemoteWorkDaysPerYear = 40m;
    private const decimal ExcuseMaxDays = 2m;
    private const decimal BirthdayMaxDays = 1m;

    public static LeaveRuleSet CreateTurkishLabourLawRuleSet(Guid tenantId, StatutoryLeaveDurations d)
    {
        var set = LeaveRuleSet.Create(tenantId, DefaultRuleSetName, isDefault: true, isSystemSeed: true).Value;
        foreach (var settings in Types(d))
        {
            set.AddType(settings);
        }

        return set;
    }

    public static IEnumerable<LeaveTypeSettings> Types(StatutoryLeaveDurations d)
    {
        var a = d.Annual;
        yield return new LeaveTypeSettings(
            LeaveTypeCode.Annual, "leave.type.annual", null, IsPaid: true, LeaveLimitType.PerYear,
            AnnualDays: a.Tier1Days, AgeExceptionEnabled: true, AgeExceptionMinDays: a.AgeExceptionMinDays,
            AccrualFrequency: AccrualFrequency.Yearly, AccrualTiming: AccrualTiming.EndOfPeriod, AccrualBasis: AccrualBasis.HireDate,
            CarryOverMode: CarryOverMode.Unlimited,
            SeniorityTiers:
            [
                new LeaveSeniorityTier(a.Tier2AfterYears, SeniorityComparison.GreaterThan, a.Tier2Days - a.Tier1Days),
                new LeaveSeniorityTier(a.Tier3AfterYears, SeniorityComparison.GreaterOrEqual, a.Tier3Days - a.Tier2Days),
            ],
            IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Excuse, "leave.type.excuse", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: ExcuseMaxDays, RequiresDescription: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Sick, "leave.type.sick", null, false, LeaveLimitType.Unlimited,
            RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.JobSearch, "leave.type.job_search", null, true, LeaveLimitType.Unlimited,
            Unit: LeaveUnit.Hourly, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Marriage, "leave.type.marriage", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.MarriageDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Bereavement, "leave.type.bereavement", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.BereavementDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Paternity, "leave.type.paternity", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.PaternityDays, CountsNonWorkingDays: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Maternity, "leave.type.maternity", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: (d.MaternityPreBirthWeeks + d.MaternityPostBirthWeeks) * DaysPerWeek, CountsNonWorkingDays: true,
            RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.PostnatalUnpaid, "leave.type.postnatal_unpaid", null, false, LeaveLimitType.Unlimited,
            CountsNonWorkingDays: true, CountsTowardAccrualService: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.PaidMilitaryService, "leave.type.paid_military_service", null, false, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.PaidMilitaryServiceDays, CountsNonWorkingDays: true, CountsTowardAccrualService: true, RequiresDocument: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Unpaid, "leave.type.unpaid", null, false, LeaveLimitType.Unlimited,
            CountsNonWorkingDays: true, CountsTowardAccrualService: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.FosterCare, "leave.type.foster_care", null, false, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.FosterCareDays, CountsNonWorkingDays: true, RequiresDocument: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.RemoteWork, "leave.type.template.remote_work", null, true, LeaveLimitType.PerYear,
            AnnualDays: RemoteWorkDaysPerYear, AccrualFrequency: AccrualFrequency.Yearly, AccrualTiming: AccrualTiming.StartOfPeriod,
            AccrualBasis: AccrualBasis.CalendarYear, CarryOverMode: CarryOverMode.Reset, RequiresDescription: true, ShowTypeNameOnCalendar: true, IsDisabled: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Birthday, "leave.type.template.birthday", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: BirthdayMaxDays, IsDisabled: true);
    }
}
