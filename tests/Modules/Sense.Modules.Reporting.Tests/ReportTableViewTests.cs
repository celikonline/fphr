using Sense.Modules.Reporting.Application;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class ReportTableViewTests
{
    private static ExportTable People(params object?[][] rows) =>
        new("Kişiler", [new ExportColumn("Ad"), new ExportColumn("Maaş", ExportColumnType.Number)], rows.Select(r => (IReadOnlyList<object?>)r).ToList());

    private static IEnumerable<object?> Names(IEnumerable<IReadOnlyList<object?>> rows) => rows.Select(r => r[0]);

    [Fact]
    public void Build_Search_FoldsTurkishCaseAndDiacritics()
    {
        var table = People(["İzin Yönetimi", 1.0], ["Çalışan", 2.0], ["Bordro", 3.0]);

        Names(ReportTableView.Build(table, "izin", null, false, 1, 50).Items).ShouldBe(["İzin Yönetimi"]);
        Names(ReportTableView.Build(table, "CALISAN", null, false, 1, 50).Items).ShouldBe(["Çalışan"]);
    }

    [Fact]
    public void Build_WhitespaceOnlySearch_DoesNotFilter()
    {
        var table = People(["Ayşe", 1.0], ["Mehmet", 2.0]);

        var result = ReportTableView.Build(table, "   ", null, false, 1, 50);

        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public void Build_SearchMatchesNumbersAndDatesInAnyColumn()
    {
        var table = new ExportTable(
            "Kayıt",
            [new ExportColumn("Ad"), new ExportColumn("Tarih", ExportColumnType.Date)],
            [["a", new DateOnly(2024, 3, 1)], ["b", new DateOnly(2023, 11, 15)]]);

        Names(ReportTableView.Build(table, "01.03.2024", null, false, 1, 50).Items).ShouldBe(["a"]);
        Names(ReportTableView.Build(table, "2023-11-15", null, false, 1, 50).Items).ShouldBe(["b"]);
    }

    [Fact]
    public void Build_SortsNumbersAscendingAndDescending_WithBlanksLast()
    {
        var table = People(["a", 20.0], ["b", null], ["c", 5.0], ["d", 100.0]);

        Names(ReportTableView.Build(table, null, 1, false, 1, 50).Items).ShouldBe(["c", "a", "d", "b"]);
        Names(ReportTableView.Build(table, null, 1, true, 1, 50).Items).ShouldBe(["d", "a", "c", "b"]);
    }

    [Fact]
    public void Build_SortsTextWithTurkishCollation()
    {
        var table = People(["Zeynep", 1.0], ["Çağla", 2.0], ["Ali", 3.0]);

        Names(ReportTableView.Build(table, null, 0, false, 1, 50).Items).ShouldBe(["Ali", "Çağla", "Zeynep"]);
    }

    [Fact]
    public void Build_Pages_AndReportsTotalOfTheFilteredSet()
    {
        var table = People(["1", 1.0], ["2", 2.0], ["3", 3.0], ["4", 4.0], ["5", 5.0]);

        var second = ReportTableView.Build(table, null, null, false, 2, 2);

        Names(second.Items).ShouldBe(["3", "4"]);
        second.TotalCount.ShouldBe(5);
        second.Page.ShouldBe(2);
        second.PageSize.ShouldBe(2);
    }

    [Fact]
    public void Build_PageBeyondTheEnd_ReturnsNoItemsButKeepsTotal()
    {
        var table = People(["1", 1.0], ["2", 2.0]);

        var result = ReportTableView.Build(table, null, null, false, 9, 50);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public void Build_EmptyTable_ReturnsEmptyResult()
    {
        var result = ReportTableView.Build(People(), null, 0, false, 1, 50);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }
}
