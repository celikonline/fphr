using Sense.Modules.Attendance.Domain.Equalization;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class EqualizationPeriodCalculatorTests
{
    [Theory]
    [InlineData(2026, 1, 15, 2026, 1, 2026, 2, 28)]
    [InlineData(2026, 2, 28, 2026, 1, 2026, 2, 28)]
    [InlineData(2026, 3, 1, 2026, 3, 2026, 4, 30)]
    [InlineData(2026, 12, 31, 2026, 11, 2026, 12, 31)]
    public void GetPeriodBounds_TwoMonthPeriods_AnchoredToJanuary(
        int year, int month, int day, int expectedStartYear, int expectedStartMonth, int expectedEndYear, int expectedEndMonth, int expectedEndDay)
    {
        var (start, end) = EqualizationPeriodCalculator.GetPeriodBounds(new DateOnly(year, month, day), periodMonths: 2, anchorMonth: 1);

        start.ShouldBe(new DateOnly(expectedStartYear, expectedStartMonth, 1));
        end.ShouldBe(new DateOnly(expectedEndYear, expectedEndMonth, expectedEndDay));
    }

    [Fact]
    public void GetPeriodBounds_FourMonthPeriods_TileWithoutGaps()
    {
        var (jan, _) = EqualizationPeriodCalculator.GetPeriodBounds(new DateOnly(2026, 2, 1), periodMonths: 4, anchorMonth: 1);
        var (may, _) = EqualizationPeriodCalculator.GetPeriodBounds(new DateOnly(2026, 5, 1), periodMonths: 4, anchorMonth: 1);
        var (sep, sepEnd) = EqualizationPeriodCalculator.GetPeriodBounds(new DateOnly(2026, 12, 31), periodMonths: 4, anchorMonth: 1);

        jan.ShouldBe(new DateOnly(2026, 1, 1));
        may.ShouldBe(new DateOnly(2026, 5, 1));
        sep.ShouldBe(new DateOnly(2026, 9, 1));
        sepEnd.ShouldBe(new DateOnly(2026, 12, 31));
    }

    // A non-January anchor (e.g. a tenant whose fiscal/HR cycle starts in March) still tiles without partial periods -
    // dates before the anchor month belong to the cycle that started the *previous* calendar year.
    [Fact]
    public void GetPeriodBounds_NonJanuaryAnchor_DatesBeforeAnchorBelongToPreviousYearsCycle()
    {
        var (start, end) = EqualizationPeriodCalculator.GetPeriodBounds(new DateOnly(2026, 1, 15), periodMonths: 2, anchorMonth: 3);

        // Cycle: 2025-03-01, 05-01, 07-01, 09-01, 11-01, 2026-01-01 - Jan 15 2026 falls in the Nov-Dec block? Let's
        // verify by construction: months since the 2025-03 anchor to 2026-01 is 10, / 2 = index 5 → start 2026-01-01.
        start.ShouldBe(new DateOnly(2026, 1, 1));
        end.ShouldBe(new DateOnly(2026, 2, 28));
    }

    // "2 aydan 4 aya geçiş" (kart senseik-014 §4, PO/hukuk onayı bekleyen varsayım): a settings change does not
    // retroactively alter a period already computed with the old value - each call is a pure function of its inputs,
    // so an already-opened EqualizationPeriod row (which captured its own bounds) is simply never recomputed.
    [Fact]
    public void GetPeriodBounds_SameDateWithDifferentPeriodMonths_YieldsDifferentBlocks()
    {
        var date = new DateOnly(2026, 3, 15);

        var (twoMonthStart, twoMonthEnd) = EqualizationPeriodCalculator.GetPeriodBounds(date, periodMonths: 2, anchorMonth: 1);
        var (fourMonthStart, fourMonthEnd) = EqualizationPeriodCalculator.GetPeriodBounds(date, periodMonths: 4, anchorMonth: 1);

        twoMonthStart.ShouldBe(new DateOnly(2026, 3, 1));
        twoMonthEnd.ShouldBe(new DateOnly(2026, 4, 30));
        fourMonthStart.ShouldBe(new DateOnly(2026, 1, 1));
        fourMonthEnd.ShouldBe(new DateOnly(2026, 4, 30));
    }

    [Fact]
    public void CountWeeksByMonday_PeriodStartingOnMonday_CountsItsOwnFirstWeek()
    {
        // 2026-03-01 dır bir Pazar; ait olduğu haftanın Pazartesi'si (2026-02-23) dönem dışında (periodStart'tan önce) -
        // sayılmaz. Dönem içindeki ilk Pazartesi 2026-03-02.
        var start = new DateOnly(2026, 3, 1);
        var end = new DateOnly(2026, 4, 30);

        var weeks = EqualizationPeriodCalculator.CountWeeksByMonday(start, end);

        // 2026-03-02, 03-09, 03-16, 03-23, 03-30, 04-06, 04-13, 04-20, 04-27 = 9 Pazartesi.
        weeks.ShouldBe(9);
    }

    [Fact]
    public void CountWeeksByMonday_PartialFirstWeek_ExcludesMondayBeforePeriodStart()
    {
        // Dönem bir Çarşamba (2026-01-01, Perşembe aslında) başlarsa, o haftanın Pazartesi'si dönemden önce kalır ve sayılmaz.
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 2, 28);

        var weeks = EqualizationPeriodCalculator.CountWeeksByMonday(start, end);

        // İlk Pazartesi 2026-01-05; son Pazartesi <= 2026-02-28 olan 2026-02-23. (05,12,19,26,02,09,16,23) = 8 hafta.
        weeks.ShouldBe(8);
    }
}
