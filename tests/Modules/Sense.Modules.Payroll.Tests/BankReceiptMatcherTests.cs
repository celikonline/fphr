using Sense.Modules.Payroll.Domain.Payments;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Dekont eşleştirme: parsing any bank's generic CSV export and matching it against the period's expected payments.</summary>
public sealed class BankReceiptMatcherTests
{
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Baran = Guid.NewGuid();

    [Fact]
    public void ParseCsv_ReadsIbanAmountAndStatus_AndSkipsTheHeaderRow()
    {
        var rows = BankReceiptMatcher.ParseCsv("IBAN;Tutar;Durum\r\nTR330006100519786457841326;38250,50;Onaylandı\r\nTR330006100519786457841327;41000.00;Basarili");

        rows.Count.ShouldBe(2);
        rows[0].Iban.ShouldBe("TR330006100519786457841326");
        rows[0].Amount.ShouldBe(38_250.50m);
        rows[0].RawStatus.ShouldBe("Onaylandı");
    }

    [Fact]
    public void ParseCsv_AlsoAcceptsCommaSeparatedRows_WithNoStatusColumn()
    {
        var rows = BankReceiptMatcher.ParseCsv("TR330006100519786457841326,38250.50\nTR330006100519786457841327,41000.00");

        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.RawStatus == null);
    }

    [Fact]
    public void Match_ConfirmsAPaymentWhoseIbanAndAmountAgree()
    {
        var expected = new[] { new ExpectedPayment(Ayse, "TR33 0006 1005 1978 6457 8413 26", 38_250.50m) };
        var rows = new[] { new BankReceiptRow("TR330006100519786457841326", 38_250.50m, "Onaylandı") };

        var result = BankReceiptMatcher.Match(expected, rows);

        result.Confirmed.ShouldHaveSingleItem().EmployeeId.ShouldBe(Ayse);
        result.AmountMismatches.ShouldBeEmpty();
        result.Rejected.ShouldBeEmpty();
        result.UnmatchedEmployeeIds.ShouldBeEmpty();
    }

    [Fact]
    public void Match_ToleratesAOneKurusRoundingDifference()
    {
        var expected = new[] { new ExpectedPayment(Ayse, "TR330006100519786457841326", 100.00m) };
        var rows = new[] { new BankReceiptRow("TR330006100519786457841326", 100.005m, null) };

        BankReceiptMatcher.Match(expected, rows).Confirmed.ShouldHaveSingleItem();
    }

    [Fact]
    public void Match_ReportsAnAmountMismatch_WhenTheReceiptDiffersBeyondTolerance()
    {
        var expected = new[] { new ExpectedPayment(Ayse, "TR330006100519786457841326", 100m) };
        var rows = new[] { new BankReceiptRow("TR330006100519786457841326", 90m, null) };

        var result = BankReceiptMatcher.Match(expected, rows);

        var mismatch = result.AmountMismatches.ShouldHaveSingleItem();
        mismatch.EmployeeId.ShouldBe(Ayse);
        mismatch.ExpectedAmount.ShouldBe(100m);
        mismatch.ReceiptAmount.ShouldBe(90m);
        result.Confirmed.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Reddedildi")]
    [InlineData("HATA")]
    [InlineData("Failed")]
    public void Match_ReportsARejection_WhenTheStatusSaysThePaymentFailed_EvenIfTheAmountMatches(string status)
    {
        var expected = new[] { new ExpectedPayment(Ayse, "TR330006100519786457841326", 100m) };
        var rows = new[] { new BankReceiptRow("TR330006100519786457841326", 100m, status) };

        var result = BankReceiptMatcher.Match(expected, rows);

        result.Rejected.ShouldHaveSingleItem().EmployeeId.ShouldBe(Ayse);
        result.Confirmed.ShouldBeEmpty();
    }

    [Fact]
    public void Match_ReportsAnUnmatchedEmployee_WhenNoRowHasTheirIban()
    {
        var expected = new[] { new ExpectedPayment(Ayse, "TR330006100519786457841326", 100m) };

        var result = BankReceiptMatcher.Match(expected, []);

        result.UnmatchedEmployeeIds.ShouldHaveSingleItem().ShouldBe(Ayse);
    }

    [Fact]
    public void Match_CountsARowThatMatchesNoExpectedPayment_AsUnrecognized()
    {
        var expected = new[] { new ExpectedPayment(Ayse, "TR330006100519786457841326", 100m) };
        var rows = new[]
        {
            new BankReceiptRow("TR330006100519786457841326", 100m, null),
            new BankReceiptRow("TR999999999999999999999999", 50m, null),
        };

        var result = BankReceiptMatcher.Match(expected, rows);

        result.Confirmed.ShouldHaveSingleItem().EmployeeId.ShouldBe(Ayse);
        result.UnrecognizedRowCount.ShouldBe(1);
    }

    [Fact]
    public void Match_HandlesTwoEmployeesIndependently()
    {
        var expected = new[]
        {
            new ExpectedPayment(Ayse, "TR330006100519786457841326", 100m),
            new ExpectedPayment(Baran, "TR330006100519786457841327", 200m),
        };
        var rows = new[] { new BankReceiptRow("TR330006100519786457841326", 100m, null) };

        var result = BankReceiptMatcher.Match(expected, rows);

        result.Confirmed.ShouldHaveSingleItem().EmployeeId.ShouldBe(Ayse);
        result.UnmatchedEmployeeIds.ShouldHaveSingleItem().ShouldBe(Baran);
    }
}
