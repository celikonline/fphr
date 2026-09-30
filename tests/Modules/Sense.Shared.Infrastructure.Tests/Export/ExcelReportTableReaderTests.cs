using ClosedXML.Excel;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.Export;

/// <summary>Genel rapor ekranı: <see cref="IReportExporter"/>'ın ürettiği xlsx'i tabloya geri okuyan port.</summary>
public sealed class ExcelReportTableReaderTests
{
    private readonly ExcelReportExporter _exporter = new();
    private readonly ExcelReportTableReader _reader = new();

    [Fact]
    public void Read_RoundTripsTypedColumnsFromTheExporter()
    {
        var table = new ExportTable(
            "Rapor",
            [
                new ExportColumn("Ad"),
                new ExportColumn("Maaş", ExportColumnType.Number),
                new ExportColumn("İşe giriş", ExportColumnType.Date),
                new ExportColumn("Talep", ExportColumnType.DateTime),
                new ExportColumn("Aktif", ExportColumnType.Boolean),
            ],
            [
                ["Ayşe", 45000.5, new DateOnly(2024, 3, 1), new DateTime(2024, 3, 1, 14, 30, 0), true],
                ["Mehmet", 38000, new DateOnly(2023, 11, 15), new DateTime(2023, 11, 15, 9, 5, 0), false],
            ]);
        var file = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        var result = _reader.Read(file.Content, TestContext.Current.CancellationToken);

        var sheet = result.ShouldHaveSingleItem();
        sheet.SheetName.ShouldBe("Rapor");
        sheet.Columns.Select(c => c.Header).ShouldBe(["Ad", "Maaş", "İşe giriş", "Talep", "Aktif"]);
        sheet.Columns.Select(c => c.Type).ShouldBe(
            [ExportColumnType.Text, ExportColumnType.Number, ExportColumnType.Date, ExportColumnType.DateTime, ExportColumnType.Boolean]);
        sheet.Rows[0].ShouldBe(new object?[] { "Ayşe", 45000.5, new DateOnly(2024, 3, 1), new DateTime(2024, 3, 1, 14, 30, 0), true });
        sheet.Rows[1].ShouldBe(new object?[] { "Mehmet", 38000.0, new DateOnly(2023, 11, 15), new DateTime(2023, 11, 15, 9, 5, 0), false });
    }

    [Fact]
    public void Read_KeepsBlankCellsNull_AndSkipsFullyEmptyRows()
    {
        var table = new ExportTable("Sayfa", [new ExportColumn("A"), new ExportColumn("B")], [["a", null], [null, null], ["b", "x"]]);
        var file = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        var sheet = _reader.Read(file.Content, TestContext.Current.CancellationToken).Single();

        sheet.Rows.Count.ShouldBe(2);
        sheet.Rows[0].ShouldBe(new object?[] { "a", null });
        sheet.Rows[1].ShouldBe(new object?[] { "b", "x" });
    }

    [Fact]
    public void Read_HeaderOnlySheet_ReturnsColumnsAndNoRows()
    {
        var table = new ExportTable("Boş", [new ExportColumn("A"), new ExportColumn("B")], []);
        var file = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        var sheet = _reader.Read(file.Content, TestContext.Current.CancellationToken).Single();

        sheet.Columns.Select(c => c.Header).ShouldBe(["A", "B"]);
        sheet.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Read_MultipleSheets_KeepsOrderAndNames()
    {
        var file = _exporter.ExportToExcel(
            "iki.xlsx",
            [new ExportTable("Birinci", [new ExportColumn("X")], [["1"]]), new ExportTable("İkinci", [new ExportColumn("Y")], [["2"]])],
            TestContext.Current.CancellationToken);

        var sheets = _reader.Read(file.Content, TestContext.Current.CancellationToken);

        sheets.Select(s => s.SheetName).ShouldBe(["Birinci", "İkinci"]);
    }

    [Fact]
    public void Read_MixedTypesInOneColumn_CollapseToText()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Karışık");
        worksheet.Cell(1, 1).Value = "Değer";
        worksheet.Cell(2, 1).Value = 5;
        worksheet.Cell(3, 1).Value = "metin";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var sheet = _reader.Read(stream.ToArray(), TestContext.Current.CancellationToken).Single();

        sheet.Columns[0].Type.ShouldBe(ExportColumnType.Text);
        sheet.Rows.Select(r => r[0]).ShouldBe(new object?[] { "5", "metin" });
    }

    [Fact]
    public void Read_TextCellWithLeadingZeros_StaysText()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Kimlik");
        worksheet.Cell(1, 1).Value = "No";
        worksheet.Cell(2, 1).Value = "00123";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var sheet = _reader.Read(stream.ToArray(), TestContext.Current.CancellationToken).Single();

        sheet.Columns[0].Type.ShouldBe(ExportColumnType.Text);
        sheet.Rows[0][0].ShouldBe("00123");
    }

    [Fact]
    public void Read_NotAWorkbook_ThrowsInvalidData() =>
        Should.Throw<InvalidDataException>(() => _reader.Read([1, 2, 3], TestContext.Current.CancellationToken));
}
