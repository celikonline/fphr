using ClosedXML.Excel;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.Export;

/// <summary>TB-1: ortak "tablo -> Excel" dışa aktarma portunun ClosedXML uygulaması.</summary>
public sealed class ExcelReportExporterTests
{
    private readonly ExcelReportExporter _exporter = new();

    [Fact]
    public void ExportToExcel_SingleTable_WritesHeaderAndRowsWithContentType()
    {
        var table = new ExportTable(
            "Çalışanlar",
            [new ExportColumn("Ad"), new ExportColumn("Maaş", ExportColumnType.Number), new ExportColumn("İşe başlama", ExportColumnType.Date)],
            [
                ["Ayşe Yılmaz", 45000.5, new DateOnly(2024, 3, 1)],
                ["Mehmet Demir", 38000, new DateOnly(2023, 11, 15)],
            ]);

        var result = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        result.FileName.ShouldBe("Çalışanlar.xlsx");
        result.ContentType.ShouldBe(ExcelReportExporter.ExcelContentType);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = workbook.Worksheet("Çalışanlar");
        sheet.Row(1).CellsUsed().Select(c => c.GetString()).ShouldBe(["Ad", "Maaş", "İşe başlama"]);
        sheet.Cell(1, 1).Style.Font.Bold.ShouldBeTrue();
        sheet.Cell(2, 1).GetString().ShouldBe("Ayşe Yılmaz");
        sheet.Cell(2, 2).GetDouble().ShouldBe(45000.5);
        sheet.Cell(2, 3).GetDateTime().ShouldBe(new DateTime(2024, 3, 1));
        sheet.Cell(2, 3).Style.NumberFormat.Format.ShouldBe("dd.mm.yyyy");
        sheet.SheetView.SplitRow.ShouldBe(1);
    }

    [Fact]
    public void ExportToExcel_NullCells_AreLeftBlank()
    {
        var table = new ExportTable("Sayfa", [new ExportColumn("A"), new ExportColumn("B")], [["değer", null]]);

        var result = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var sheet = workbook.Worksheet(1);
        sheet.Cell(2, 1).GetString().ShouldBe("değer");
        sheet.Cell(2, 2).IsEmpty().ShouldBeTrue();
    }

    [Fact]
    public void ExportToExcel_MultipleSheets_SanitizesAndDeduplicatesNames()
    {
        var longName = new string('a', 40);
        var sheets = new[]
        {
            new ExportTable("Rapor[1]:a/b*c?", [new ExportColumn("X")], [["1"]]),
            new ExportTable("Rapor[1]:a/b*c?", [new ExportColumn("X")], [["2"]]),
            new ExportTable(longName, [new ExportColumn("X")], [["3"]]),
        };

        var result = _exporter.ExportToExcel("rapor.xlsx", sheets, TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(result.Content));
        var names = workbook.Worksheets.Select(w => w.Name).ToList();
        names.Count.ShouldBe(3);
        names.Distinct().Count().ShouldBe(3);
        names.ShouldAllBe(n => n.Length <= 31 && !n.Any(c => "[]:*?/\\".Contains(c)));
    }

    [Fact]
    public void ExportToExcel_EmptySheetList_Throws() =>
        Should.Throw<ArgumentException>(() => _exporter.ExportToExcel("x.xlsx", [], TestContext.Current.CancellationToken));
}
