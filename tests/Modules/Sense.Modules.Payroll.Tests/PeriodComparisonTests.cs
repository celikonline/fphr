using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>The month-over-month comparison and its 5% deviation review.</summary>
public sealed class PeriodComparisonTests
{
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Baran = Guid.NewGuid();
    private static readonly Guid Ceren = Guid.NewGuid();

    private static PayslipFigures Figures(Guid id, decimal gross, decimal net, decimal cost = 0m) => new(id, gross, net, cost);

    [Fact]
    public void ASmallChange_IsNotFlagged_ButAChangeOverTheThresholdIs()
    {
        var current = new[] { Figures(Ayse, 51_000m, 40_000m), Figures(Baran, 60_000m, 46_000m) };
        var previous = new[] { Figures(Ayse, 50_000m, 39_000m), Figures(Baran, 50_000m, 40_000m) };

        var result = PeriodComparisonCalculator.Compare(current, previous);

        var ayse = result.Rows.Single(r => r.EmployeeId == Ayse);
        ayse.GrossChangePercent.ShouldBe(2.00m); // 1,000 / 50,000
        ayse.NetChangePercent.ShouldBe(2.56m); // 1,000 / 39,000 = 2.564...
        ayse.ExceedsThreshold.ShouldBeFalse();

        var baran = result.Rows.Single(r => r.EmployeeId == Baran);
        baran.GrossChangePercent.ShouldBe(20.00m);
        baran.ExceedsThreshold.ShouldBeTrue();
    }

    [Fact]
    public void ExactlyAtTheThreshold_IsNotFlagged()
    {
        var result = PeriodComparisonCalculator.Compare([Figures(Ayse, 52_500m, 40_000m)], [Figures(Ayse, 50_000m, 40_000m)]);

        result.Rows.Single().GrossChangePercent.ShouldBe(5.00m);
        result.Rows.Single().ExceedsThreshold.ShouldBeFalse();
    }

    [Fact]
    public void NewAndLeftEmployees_AreAlwaysFlagged_AndListedFirst()
    {
        var current = new[] { Figures(Ayse, 50_000m, 40_000m), Figures(Ceren, 30_000m, 24_000m) };
        var previous = new[] { Figures(Ayse, 50_000m, 40_000m), Figures(Baran, 45_000m, 36_000m) };

        var result = PeriodComparisonCalculator.Compare(current, previous);

        result.Rows.Take(2).Select(r => (r.EmployeeId, r.Kind)).ShouldBe(
            [(Ceren, ComparisonKind.New), (Baran, ComparisonKind.Left)], ignoreOrder: true);
        result.Rows.Last().EmployeeId.ShouldBe(Ayse);
        result.Rows.Single(r => r.EmployeeId == Ceren).PreviousGross.ShouldBeNull();
        result.Rows.Single(r => r.EmployeeId == Baran).CurrentGross.ShouldBeNull();
    }

    [Fact]
    public void ChangeFromZero_HasNoPercentage_ButCountsAsExceeding()
    {
        var result = PeriodComparisonCalculator.Compare([Figures(Ayse, 10_000m, 8_000m)], [Figures(Ayse, 0m, 0m)]);

        var row = result.Rows.Single();
        row.GrossChangePercent.ShouldBeNull();
        row.ExceedsThreshold.ShouldBeTrue();
    }

    [Fact]
    public void Totals_SumBothPeriods()
    {
        var current = new[] { Figures(Ayse, 50_000m, 40_000m, 60_000m), Figures(Baran, 30_000m, 24_000m, 36_000m) };
        var previous = new[] { Figures(Ayse, 48_000m, 38_000m, 57_000m) };

        var result = PeriodComparisonCalculator.Compare(current, previous);

        result.Current.ShouldBe(new ComparisonTotals(2, 80_000m, 64_000m, 96_000m));
        result.Previous.ShouldBe(new ComparisonTotals(1, 48_000m, 38_000m, 57_000m));
    }

    [Fact]
    public void ACustomThreshold_ChangesWhatIsFlagged()
    {
        var current = new[] { Figures(Ayse, 51_000m, 40_000m) };
        var previous = new[] { Figures(Ayse, 50_000m, 40_000m) };

        PeriodComparisonCalculator.Compare(current, previous, 1m).Rows.Single().ExceedsThreshold.ShouldBeTrue();
        PeriodComparisonCalculator.Compare(current, previous, 5m).Rows.Single().ExceedsThreshold.ShouldBeFalse();
    }

    [Fact]
    public void ANegativeThreshold_IsRejected()
    {
        Should.Throw<Exception>(() => PeriodComparisonCalculator.Compare([], [], -1m));
    }
}
