using System.Globalization;
using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Which fesih (termination) category a severance/notice estimate is for (senseik-011, F-06 "İşten ayrılış
/// sihirbazı"): coarser than <c>Employee.Domain.Employees.TerminationType</c> because it also distinguishes the one
/// case that type does not - md.25/II ahlak ve iyiniyet kurallarına aykırılık, the only cause that forfeits both
/// kıdem and ihbar tazminatı. The wizard/caller picks the category explicitly (this is a pre-termination "ne olur"
/// estimate - nothing is persisted yet, so nothing to derive the category from).
/// </summary>
public enum SeveranceCauseCategory
{
    /// <summary>İş K. m.17/18 - işveren feshi, geçerli neden veya süreli fesih bildirimi: kıdem ✓, ihbar ✓.</summary>
    EmployerNoticeBased = 0,

    /// <summary>İş K. m.24 - işçinin haklı nedenle feshi: kıdem ✓, ihbar ✗.</summary>
    EmployeeJustCause = 1,

    /// <summary>İş K. m.25/I,III,IV (sağlık, zorlayıcı sebep, tutukluluk) - işverenin haklı nedenle feshi: kıdem ✓, ihbar ✗.</summary>
    EmployerJustCauseOther = 2,

    /// <summary>İş K. m.25/II - ahlak ve iyiniyet kurallarına aykırılık: kıdem ✗, ihbar ✗.</summary>
    ImmoralOrBadFaith = 3,

    /// <summary>İşçinin haklı neden olmaksızın istifası: kıdem ✗, ihbar ✗ (işçinin ihbar borcu bu tahminin kapsamı dışında).</summary>
    EmployeeResignation = 4,
}

/// <summary>One kıdem-günü-üstü ihbar-hafta eşiği ("İhbar önel tablosu", İş K. m.17): <see cref="Weeks"/> applies while seniority days are below <see cref="UpperBoundDays"/> (null = unbounded, last tier).</summary>
public sealed record NoticeWeeksTier(int? UpperBoundDays, int Weeks);

/// <summary>
/// Resolved <c>Severance.*</c>/<c>Notice.*</c> LegislationParameter values (ADR 0008) for one estimate date, as plain
/// values - mirrors how <see cref="PayrollLegislationSnapshot"/> keeps Domain free of the Shared.Contracts DB dependency.
/// </summary>
public sealed record SeveranceLegislationSnapshot(decimal CeilingGross, IReadOnlyList<NoticeWeeksTier> NoticeWeeksTiers, DateOnly AsOf);

/// <summary>
/// Plain input for <see cref="SeveranceCalculator.Calculate"/>: one employee, one hypothetical exit date. Counted
/// seniority days and the unused annual leave balance are resolved by the Application layer (Employee/Leave Contracts
/// ports) - Domain takes plain values, same seam as <see cref="PayrollCalculationInput"/>.
/// </summary>
/// <param name="SeniorityDays">Kıdem tazminatı kıdemi ("severance-purpose" counted days - severance-paid prior periods excluded); both ends inclusive.</param>
/// <param name="CauseCategory">Which entitlement rule applies (see <see cref="SeveranceCauseCategory"/>).</param>
/// <param name="DailyGrossWage">Exit-date gross daily wage, resolved by Application (including gross-up for net salary records).</param>
/// <param name="UnusedAnnualLeaveDays">Kullanılmamış yıllık izin günü (İş K. m.59); paid regardless of <paramref name="CauseCategory"/> - earned leave is never forfeited by the reason for exit.</param>
public sealed record SeveranceInput(int SeniorityDays, SeveranceCauseCategory CauseCategory, decimal DailyGrossWage, decimal UnusedAnnualLeaveDays,
    decimal RegularBenefitsMonthlyGross = 0m);

/// <summary>Result of <see cref="SeveranceCalculator.Calculate"/>: the full step trace plus the headline amounts the estimate endpoint returns.</summary>
public sealed record SeveranceCalculationResult(
    IReadOnlyList<TraceStep> Steps, decimal SeveranceAmount, decimal NoticeAmount, decimal UnusedLeaveAmount, decimal TotalAmount)
{
    public TraceStep Step(string code) => Steps.Single(s => s.Code == code);
}

public static class SeveranceTraceStepCodes
{
    public const string SeniorityYears = "SENIORITY_YEARS";
    public const string Eligibility = "ELIGIBILITY";
    public const string SeverancePerYear = "SEVERANCE_PER_YEAR";
    public const string SeveranceAmount = "SEVERANCE_AMOUNT";
    public const string NoticeWeeks = "NOTICE_WEEKS";
    public const string NoticeAmount = "NOTICE_AMOUNT";
    public const string UnusedLeaveAmount = "UNUSED_LEAVE_AMOUNT";
    public const string Total = "TOTAL";
}

/// <summary>
/// Pure kıdem/ihbar/kullanılmamış-izin-ücreti estimate engine (senseik-011, F-06 "İşten ayrılış sihirbazı"): a
/// read-only "ne olur" projection, never a payslip line - no DB access, all statutory values come in via
/// <see cref="SeveranceLegislationSnapshot"/>, resolved by the Application layer from ILegislationParameterReader
/// (ADR 0008), same seam as <see cref="PayrollCalculator"/>/<see cref="IncomeTaxMath"/>. Kötüniyet/sendikal tazminat
/// is explicitly out of scope (senseik-011 acceptance criteria).
/// </summary>
public static class SeveranceCalculator
{
    /// <summary>İş K. m.14 - kıdem tazminatına hak kazanmak için gereken asgari kıdem (bir tam yıl).</summary>
    private const int MinSeniorityDaysForSeverance = 365;

    private const int DaysPerYear = 365;
    private const int SeveranceDaysPerYear = 30;
    private const int DaysPerWeek = 7;

    public static SeveranceCalculationResult Calculate(SeveranceInput input, SeveranceLegislationSnapshot p)
    {
        Guard.Against(input.SeniorityDays < 0, "SeniorityDays must not be negative.");
        Guard.Against(input.DailyGrossWage < 0m, "DailyGrossWage must not be negative.");
        Guard.Against(input.UnusedAnnualLeaveDays < 0m, "UnusedAnnualLeaveDays must not be negative.");
        Guard.Against(input.RegularBenefitsMonthlyGross < 0m, "RegularBenefitsMonthlyGross must not be negative.");
        if (!Enum.IsDefined(input.CauseCategory))
        {
            throw new NotSupportedException($"Severance cause category '{input.CauseCategory}' is not a known value.");
        }

        var steps = new List<TraceStep>();
        var order = 1;

        var years = (decimal)input.SeniorityDays / DaysPerYear;
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.SeniorityYears, "payroll.severance.trace.seniority_years",
            Map(("seniorityDays", input.SeniorityDays)),
            "seniorityDays / 365",
            Round(years), []));

        var (severanceEligible, noticeEligible) = Eligibility(input.CauseCategory);
        var meetsMinSeniority = input.SeniorityDays >= MinSeniorityDaysForSeverance;
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.Eligibility, "payroll.severance.trace.eligibility",
            new Dictionary<string, string>
            {
                ["causeCategory"] = input.CauseCategory.ToString(),
                ["meetsMinSeniority"] = meetsMinSeniority ? "1" : "0",
            },
            "fesih türü ve en az 1 yıllık kıdem şartına göre kıdem/ihbar hak edişi (İş K. m.14/17/24/25)",
            severanceEligible && meetsMinSeniority ? 1m : 0m, []));

        var dressedDailyGrossWage = input.DailyGrossWage + input.RegularBenefitsMonthlyGross / SeveranceDaysPerYear;
        var thirtyDayAmount = input.DailyGrossWage * SeveranceDaysPerYear + input.RegularBenefitsMonthlyGross;
        var severancePerYear = Math.Min(thirtyDayAmount, p.CeilingGross);
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.SeverancePerYear, "payroll.severance.trace.per_year",
            Map(("dailyGrossWage", input.DailyGrossWage), ("regularBenefitsMonthlyGross", input.RegularBenefitsMonthlyGross),
                ("thirtyDayAmount", thirtyDayAmount), ("ceilingGross", p.CeilingGross)),
            "min(dailyGrossWage * 30 + regularBenefitsMonthlyGross, Severance.CeilingGross)",
            Round(severancePerYear), [Ref("Severance.CeilingGross", p.CeilingGross, p)]));

        var severanceAmount = severanceEligible && meetsMinSeniority ? Round(severancePerYear * years) : 0m;
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.SeveranceAmount, "payroll.severance.trace.severance_amount",
            Map(("severancePerYear", severancePerYear), ("years", years)),
            "hak ediş varsa: severancePerYear * years, aksi halde 0",
            severanceAmount, []));

        var noticeWeeks = NoticeWeeksFor(input.SeniorityDays, p.NoticeWeeksTiers);
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.NoticeWeeks, "payroll.severance.trace.notice_weeks",
            Map(("seniorityDays", input.SeniorityDays)),
            "kıdem gününe göre ihbar önel tablosu (İş K. m.17, Notice.WeeksBySeniorityTiers)",
            noticeWeeks, []));

        var noticeAmount = noticeEligible ? Round(dressedDailyGrossWage * noticeWeeks * DaysPerWeek) : 0m;
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.NoticeAmount, "payroll.severance.trace.notice_amount",
            Map(("dailyGrossWage", input.DailyGrossWage), ("dressedDailyGrossWage", dressedDailyGrossWage), ("noticeWeeks", noticeWeeks)),
            "hak ediş varsa: dressedDailyGrossWage * noticeWeeks * 7, aksi halde 0",
            noticeAmount, []));

        // Unused annual leave pay (İş K. m.59): always computed, independent of CauseCategory - earned leave is
        // owed on any exit, including ImmoralOrBadFaith/EmployeeResignation where severance/notice are forfeited.
        var unusedLeaveAmount = Round(input.DailyGrossWage * input.UnusedAnnualLeaveDays);
        steps.Add(new TraceStep(
            order++, SeveranceTraceStepCodes.UnusedLeaveAmount, "payroll.severance.trace.unused_leave_amount",
            Map(("dailyGrossWage", input.DailyGrossWage), ("unusedAnnualLeaveDays", input.UnusedAnnualLeaveDays)),
            "dailyGrossWage * kullanılmamış yıllık izin günü (İş K. m.59; fesih türünden bağımsız)",
            unusedLeaveAmount, []));

        var total = Round(severanceAmount + noticeAmount + unusedLeaveAmount);
        steps.Add(new TraceStep(
            order, SeveranceTraceStepCodes.Total, "payroll.severance.trace.total",
            Map(("severanceAmount", severanceAmount), ("noticeAmount", noticeAmount), ("unusedLeaveAmount", unusedLeaveAmount)),
            "kıdem tazminatı + ihbar tazminatı + kullanılmamış izin ücreti",
            total, []));

        return new SeveranceCalculationResult(steps, severanceAmount, noticeAmount, unusedLeaveAmount, total);
    }

    private static (bool SeveranceEligible, bool NoticeEligible) Eligibility(SeveranceCauseCategory category) => category switch
    {
        SeveranceCauseCategory.EmployerNoticeBased => (true, true),
        SeveranceCauseCategory.EmployeeJustCause => (true, false),
        SeveranceCauseCategory.EmployerJustCauseOther => (true, false),
        SeveranceCauseCategory.ImmoralOrBadFaith => (false, false),
        SeveranceCauseCategory.EmployeeResignation => (false, false),
        _ => (false, false),
    };

    private static int NoticeWeeksFor(int seniorityDays, IReadOnlyList<NoticeWeeksTier> tiers)
    {
        foreach (var tier in tiers)
        {
            if (tier.UpperBoundDays is null || seniorityDays < tier.UpperBoundDays)
            {
                return tier.Weeks;
            }
        }

        return tiers.Count > 0 ? tiers[^1].Weeks : 0;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Map(params (string Key, decimal Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => Format(p.Value));

    private static Dictionary<string, string> Map(params (string Key, int Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value.ToString(CultureInfo.InvariantCulture));

    private static TraceParameterRef Ref(string key, decimal value, SeveranceLegislationSnapshot p) => new(key, value, p.AsOf);
}
