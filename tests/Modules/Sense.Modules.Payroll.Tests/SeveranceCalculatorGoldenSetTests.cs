using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// Golden-set snapshot tests for <see cref="SeveranceCalculator"/> (senseik-011, F-06 "İşten ayrılış sihirbazı" -
/// kıdem tazminatı + ihbar tazminatı + kullanılmamış yıllık izin ücreti). Domain-layer only - no DB, no
/// Testcontainers. DailyGrossWage/CeilingGross are round numbers by design so every amount below can be
/// hand-verified from the fraction shown in each test's comment, mirroring PayrollCalculatorGoldenSetTests.
/// </summary>
public sealed class SeveranceCalculatorGoldenSetTests
{
    [Fact]
    public void RecurringBenefits_IncreaseSeveranceAndNotice_ButNotUnusedLeavePay()
    {
        var input = new SeveranceInput(365, SeveranceCauseCategory.EmployerNoticeBased, 1_000m, 10m, RegularBenefitsMonthlyGross: 3_000m);
        var result = SeveranceCalculator.Calculate(input, Snapshot);
        result.SeveranceAmount.ShouldBe(33_000m);
        result.NoticeAmount.ShouldBe(30_800m); // (1,000 + 3,000/30) * 4 weeks * 7 days
        result.UnusedLeaveAmount.ShouldBe(10_000m);
    }

    [Fact]
    public void TheSeveranceCeiling_AppliesAfterRecurringBenefitsAreIncluded()
    {
        var input = new SeveranceInput(365, SeveranceCauseCategory.EmployerNoticeBased, 1_000m, 10m, RegularBenefitsMonthlyGross: 30_000m);
        var result = SeveranceCalculator.Calculate(input, Snapshot);
        result.SeveranceAmount.ShouldBe(55_000m);
        result.NoticeAmount.ShouldBe(56_000m); // notice is not capped by the severance ceiling
        result.UnusedLeaveAmount.ShouldBe(10_000m);
    }
    private const decimal CeilingGross = 55_000.00m;
    private const decimal DailyGrossWage = 1_000.00m;

    /// <summary>İş K. m.17 tablosu: 183/548/1095 gün eşikleri (~6 ay / 1,5 yıl / 3 yıl), same shape as the seeded Notice.WeeksBySeniorityTiers JSON.</summary>
    private static readonly SeveranceLegislationSnapshot Snapshot = new(
        CeilingGross,
        [
            new NoticeWeeksTier(183, 2),
            new NoticeWeeksTier(548, 4),
            new NoticeWeeksTier(1095, 6),
            new NoticeWeeksTier(null, 8),
        ],
        new DateOnly(2026, 1, 1));

    [Fact]
    public void HalfYearSeniority_NoSeveranceEntitlement_ButNoticeStillApplies()
    {
        // 180 gün < 365 gün (İş K. m.14 asgari kıdem şartı) -> kıdem tazminatı hak edilmez. İhbar süresi kıdem
        // eşiğine bağlı değildir (m.17): 180 < 183 -> 2 hafta, işveren bildirimli fesihte hâlâ ödenir.
        var input = new SeveranceInput(180, SeveranceCauseCategory.EmployerNoticeBased, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.SeveranceAmount.ShouldBe(0m);
        result.Step(SeveranceTraceStepCodes.NoticeWeeks).Output.ShouldBe(2m);
        result.NoticeAmount.ShouldBe(14_000.00m); // 1,000 * 2 hafta * 7 gün
        result.UnusedLeaveAmount.ShouldBe(0m);
        result.TotalAmount.ShouldBe(14_000.00m);
    }

    [Fact]
    public void StandardOneAndHalfYearSeniority_SeveranceAndNoticeBothApply()
    {
        // 550 gün (~1,5 yıl, 548 gün eşiğinin hemen üstü) -> ihbar tablosunda 6 hafta dilimi.
        // Kıdem: 30,000 (=1,000*30) tavanın (55,000) altında, kırpılmaz. severanceAmount = 30,000 * 550/365
        //      = 3,300,000/73 = 45,205.4794... -> 45,205.48.
        var input = new SeveranceInput(550, SeveranceCauseCategory.EmployerNoticeBased, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.Step(SeveranceTraceStepCodes.SeverancePerYear).Output.ShouldBe(30_000.00m);
        result.SeveranceAmount.ShouldBe(45_205.48m);
        result.Step(SeveranceTraceStepCodes.NoticeWeeks).Output.ShouldBe(6m);
        result.NoticeAmount.ShouldBe(42_000.00m); // 1,000 * 6 hafta * 7 gün
        result.TotalAmount.ShouldBe(87_205.48m);
    }

    [Fact]
    public void SixYearSeniority_StandardCalculation_NoClamp()
    {
        // 2,190 gün = tam 6 yıl. 30,000 (tavanın altında) * 6 = 180,000. 6 yıl >= 1095 gün -> son dilim, 8 hafta.
        var input = new SeveranceInput(2190, SeveranceCauseCategory.EmployerNoticeBased, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.Step(SeveranceTraceStepCodes.SeniorityYears).Output.ShouldBe(6.00m);
        result.SeveranceAmount.ShouldBe(180_000.00m);
        result.NoticeAmount.ShouldBe(56_000.00m); // 1,000 * 8 hafta * 7 gün
        result.TotalAmount.ShouldBe(236_000.00m);
    }

    [Fact]
    public void TwelveYearSeniority_AboveCeiling_SeverancePerYearIsClamped()
    {
        // 4,380 gün = tam 12 yıl. Günlük ücret 3,000 iken 30 günlük tutar (90,000) tavanı (55,000) aşıyor ->
        // severancePerYear tavana kırpılır: 55,000 * 12 = 660,000 (kırpma olmasaydı 90,000*12 = 1,080,000 olurdu).
        const decimal highDailyWage = 3_000.00m;
        var input = new SeveranceInput(4380, SeveranceCauseCategory.EmployerNoticeBased, highDailyWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.Step(SeveranceTraceStepCodes.SeverancePerYear).Output.ShouldBe(CeilingGross); // kırpıldı, 90,000 değil
        result.SeveranceAmount.ShouldBe(660_000.00m);
        result.NoticeAmount.ShouldBe(168_000.00m); // 3,000 * 8 hafta * 7 gün
        result.TotalAmount.ShouldBe(828_000.00m);
    }

    [Fact]
    public void DiscontinuousPeriods_2485DaysExample_FractionalYearsCalculatedCorrectly()
    {
        // Kariyer sekmesindeki "Kıdem tazminatı kıdemi" örneği (11.3.03, fasılalı dönemler birleşik): 2,485 gün
        // (Employee.CountedSeniorityDays zaten kıdem tazminatı ödenmiş dönemleri hariç tutarak bu sayıyı üretir -
        // bu test yalnızca hesap motorunun kesirli yıl matematiğini doğrular, dönem birleştirmeyi değil).
        // years = 2485/365 = 497/73 = 6.808219... -> trace'te 6.81 olarak yuvarlanır.
        // severanceAmount = 30,000 * 497/73 = 14,910,000/73 = 204,246.5753... -> 204,246.58.
        // 2,485 gün >= 1,095 gün -> son dilim, 8 hafta -> noticeAmount = 1,000 * 8 * 7 = 56,000.
        var input = new SeveranceInput(2485, SeveranceCauseCategory.EmployerNoticeBased, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.Step(SeveranceTraceStepCodes.SeniorityYears).Output.ShouldBe(6.81m);
        result.SeveranceAmount.ShouldBe(204_246.58m);
        result.NoticeAmount.ShouldBe(56_000.00m);
        result.TotalAmount.ShouldBe(260_246.58m);
    }

    [Fact]
    public void EmployeeJustCause_SeveranceOwed_NoticeNotOwed()
    {
        // İş K. m.24 - işçinin haklı nedenle feshi: kıdem tazminatı hak edilir, ihbar tazminatı hak edilmez.
        // severanceAmount = 30,000 * 1000/365 = 6,000,000/73 = 82,191.7808... -> 82,191.78.
        var input = new SeveranceInput(1000, SeveranceCauseCategory.EmployeeJustCause, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.SeveranceAmount.ShouldBe(82_191.78m);
        result.NoticeAmount.ShouldBe(0m);
        result.TotalAmount.ShouldBe(82_191.78m);
    }

    [Fact]
    public void ImmoralOrBadFaithTermination_NeitherSeveranceNorNoticeOwed()
    {
        // İş K. m.25/II - ahlak ve iyiniyet kurallarına aykırılık: ne kıdem ne ihbar tazminatı ödenir.
        var input = new SeveranceInput(1000, SeveranceCauseCategory.ImmoralOrBadFaith, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.SeveranceAmount.ShouldBe(0m);
        result.NoticeAmount.ShouldBe(0m);
        result.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public void UnusedAnnualLeavePay_OwedRegardlessOfCause_EvenWhenSeveranceAndNoticeAreForfeited()
    {
        // İş K. m.59 - kullanılmamış yıllık izin ücreti fesih sebebinden bağımsızdır: ahlak/iyiniyet feshinde bile
        // (kıdem/ihbar sıfırlansa da) kazanılmış izin ücreti ödenir. unusedLeaveAmount = 1,000 * 15 = 15,000.
        var input = new SeveranceInput(1000, SeveranceCauseCategory.ImmoralOrBadFaith, DailyGrossWage, UnusedAnnualLeaveDays: 15m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.SeveranceAmount.ShouldBe(0m);
        result.NoticeAmount.ShouldBe(0m);
        result.UnusedLeaveAmount.ShouldBe(15_000.00m);
        result.TotalAmount.ShouldBe(15_000.00m);
    }

    [Fact]
    public void Steps_AreProducedInFixedOrder()
    {
        var input = new SeveranceInput(1000, SeveranceCauseCategory.EmployerNoticeBased, DailyGrossWage, UnusedAnnualLeaveDays: 5m);

        var result = SeveranceCalculator.Calculate(input, Snapshot);

        result.Steps.Select(s => s.Code).ShouldBe(
        [
            SeveranceTraceStepCodes.SeniorityYears, SeveranceTraceStepCodes.Eligibility, SeveranceTraceStepCodes.SeverancePerYear,
            SeveranceTraceStepCodes.SeveranceAmount, SeveranceTraceStepCodes.NoticeWeeks, SeveranceTraceStepCodes.NoticeAmount,
            SeveranceTraceStepCodes.UnusedLeaveAmount, SeveranceTraceStepCodes.Total,
        ]);
    }

    [Fact]
    public void UnknownCauseCategory_Throws()
    {
        var input = new SeveranceInput(1000, (SeveranceCauseCategory)99, DailyGrossWage, UnusedAnnualLeaveDays: 0m);

        Should.Throw<NotSupportedException>(() => SeveranceCalculator.Calculate(input, Snapshot));
    }
}
