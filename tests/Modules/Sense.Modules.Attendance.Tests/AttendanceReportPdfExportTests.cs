using Sense.Modules.Attendance.Infrastructure.Reporting;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// TK-004: pure unit tests for <see cref="AttendanceReportPdfExport"/> - no QuestPDF rendering, no database. Verifies
/// the "format=pdf" opt-in flag and that the PDF table is built from the exact same ExportColumn/row data the Excel
/// path uses (see AttendanceLeaveCrossCheckAndReportingTests for the end-to-end PDF rendering + Excel equivalence).
/// </summary>
public sealed class AttendanceReportPdfExportTests
{
    [Theory]
    [InlineData("pdf", true)]
    [InlineData("PDF", true)]
    [InlineData("excel", false)]
    [InlineData("", false)]
    public void IsPdfRequested_ReadsFormatParameterCaseInsensitively(string format, bool expected)
    {
        var parameters = new Dictionary<string, string>();
        if (format.Length > 0)
        {
            parameters["format"] = format;
        }

        AttendanceReportPdfExport.IsPdfRequested(parameters).ShouldBe(expected);
    }

    [Fact]
    public void IsPdfRequested_NoFormatParameter_ReturnsFalse_ExistingExcelBehaviourUnchanged()
    {
        AttendanceReportPdfExport.IsPdfRequested(new Dictionary<string, string>()).ShouldBeFalse();
    }

    [Fact]
    public void BuildDocument_CarriesSameColumnsAndCellsAsExcelExport()
    {
        var columns = new List<ExportColumn>
        {
            new("Ad"),
            new("Soyad"),
            new("Çalışılan gün", ExportColumnType.Number),
        };
        var rows = new List<IReadOnlyList<object?>>
        {
            new List<object?> { "Ayşe", "Yılmaz", 21m },
        };

        var document = AttendanceReportPdfExport.BuildDocument(
            "Aylık Puantaj Özeti", columns, rows, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        document.Title.ShouldBe("Aylık Puantaj Özeti");
        var table = document.Blocks.ShouldHaveSingleItem().ShouldBeOfType<SimplePdfTable>();
        table.Columns.ShouldBe(["Ad", "Soyad", "Çalışılan gün"]);
        table.Rows.ShouldHaveSingleItem().ShouldBe(["Ayşe", "Yılmaz", "21"]);
        table.RightAlignedColumns.ShouldBe([2]); // yalnız Number kolonu sağa yaslı
        document.Footer.ShouldBe("01.09.2026 - 30.09.2026");
    }

    [Fact]
    public void BuildDocument_NullCell_RendersAsEmptyString()
    {
        var columns = new List<ExportColumn> { new("Kapandı mı") };
        var rows = new List<IReadOnlyList<object?>> { new List<object?> { null } };

        var document = AttendanceReportPdfExport.BuildDocument("Başlık", columns, rows, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var table = (SimplePdfTable)document.Blocks[0];
        table.Rows.ShouldHaveSingleItem().ShouldBe([string.Empty]);
    }
}
