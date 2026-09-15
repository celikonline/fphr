using System.Globalization;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Application.ImportJobs;
using Sense.Shared.Contracts.Import;
using Shouldly;
using Xunit;

namespace Sense.Modules.DataTransfer.Tests;

public sealed class ImportParsingAndMappingTests
{
    [Theory]
    [InlineData("15.06.1990", 1990, 6, 15)]
    [InlineData("5.6.1990", 1990, 6, 5)]
    [InlineData("15/06/1990", 1990, 6, 15)]
    [InlineData("1990-06-15", 1990, 6, 15)]
    [InlineData(" 21/09/2019 09:00 ", 2019, 9, 21)]
    [InlineData("21.09.2019 18:30", 2019, 9, 21)]
    [InlineData("2019-09-21T09:00", 2019, 9, 21)]
    public void TryParseDate_AcceptsConfiguredFormats(string text, int year, int month, int day)
    {
        ImportValueParser.TryParseDate(new ImportCell(text), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("1 Ocak 2020")]
    [InlineData("Oca-20")]
    [InlineData("31.02.2024")]
    [InlineData("06/15/1990")]
    [InlineData("43831")]
    [InlineData("")]
    public void TryParseDate_RejectsTextualOrInvalidDates(string text) =>
        ImportValueParser.TryParseDate(new ImportCell(text), out _).ShouldBeFalse();

    [Fact]
    public void TryParseDate_AcceptsNativeSpreadsheetDateCells()
    {
        ImportValueParser.TryParseDate(new ImportCell("whatever", new DateTime(2024, 3, 1, 9, 0, 0)), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2024, 3, 1));
    }

    [Theory]
    [InlineData("10000000146", true)]
    [InlineData(" 100 000 001 46 ", true)]
    [InlineData("12345678901", false)]
    [InlineData("01234567890", false)]
    [InlineData("1000000014", false)]
    [InlineData("abc", false)]
    public void TryParseNationalId_ValidatesChecksumAndTolaratesSpaces(string text, bool valid)
    {
        ImportValueParser.TryParseNationalId(text, out var parsed).ShouldBe(valid);
        if (valid)
        {
            parsed.ShouldBe("10000000146");
        }
    }

    [Theory]
    [InlineData("Satış Ekibi", "  satış   EKİBİ ", true)]
    [InlineData("İstanbul", "istanbul", true)]
    [InlineData("Işık", "IŞIK", true)]
    [InlineData("National ID", "national id", true)]
    [InlineData("Satış", "Satis", false)]
    public void TextEquals_IsCaseAndWhitespaceInsensitive(string left, string right, bool expected)
    {
        ImportValueParser.TextEquals(left, right).ShouldBe(expected);
        (ImportValueParser.MatchKey(left) == ImportValueParser.MatchKey(right)).ShouldBe(expected);
    }

    [Fact]
    public void MatchOption_ReturnsCanonicalOption() =>
        ImportValueParser.MatchOption(" ar-ge ", ["Satış", "AR-GE"]).ShouldBe("AR-GE");

    [Fact]
    public void IsKeyRow_RequiresRowTwoMostlyTemplateKeys()
    {
        string[] keys = ["firstName", "lastName", "nationalId"];
        ColumnAutoMapper.IsKeyRow(Row(2, "firstName", "LASTNAME", "custom"), keys).ShouldBeTrue();
        ColumnAutoMapper.IsKeyRow(Row(2, "Ayşe", "Yılmaz", "10000000146"), keys).ShouldBeFalse();
        ColumnAutoMapper.IsKeyRow(Row(3, "firstName", "lastName"), keys).ShouldBeFalse();
        ColumnAutoMapper.IsKeyRow(Row(2, "firstName", "x", "y"), keys).ShouldBeFalse();
    }

    [Fact]
    public void Suggest_MatchesByKeyRowThenTurkishOrEnglishHeader_EachTargetOnce()
    {
        var columns = new[]
        {
            Column("firstName", "Ad", "First name"),
            Column("lastName", "Soyad", "Last name"),
            Column("nationalId", "Kimlik numarası", "National ID"),
            Column("orgUnit:Department", "Departman"),
        };

        // Renamed header "İsim" is still mapped through its key row cell; "national id" matches the English header.
        var headers = new[] { "İsim", "  SOYAD ", "national   id", "Departman", "Ad", "Notlar" };
        var keyRow = Row(2, "firstName", "", "", "", "", "");

        var suggestions = ColumnAutoMapper.Suggest(headers, keyRow, headers.Length, columns);

        suggestions.Select(s => s.TargetKey).ShouldBe(["firstName", "lastName", "nationalId", "orgUnit:Department", null, null]);
        suggestions.Take(4).ShouldAllBe(s => s.AutoMatched);
        suggestions[4].AutoMatched.ShouldBeFalse();
    }

    [Fact]
    public void BuildSourceColumns_CollectsSamplesAndSkipsFullyEmptyColumns()
    {
        var sheet = new Spreadsheet(
            ["Ad", "", "Kimlik numarası"],
            [Row(2, "Ali", "", "10000000146"), Row(3, "Veli", "", ""), Row(4, "Can"), Row(5, "Cem")]);
        var columns = new[] { Column("firstName", "Ad"), Column("nationalId", "Kimlik numarası") };

        var result = ColumnAutoMapper.BuildSourceColumns(sheet, hasKeyRow: false, columns);

        result.Select(c => c.Index).ShouldBe([0, 2]);
        result[0].Samples.ShouldBe(["Ali", "Veli", "Can"]);
        result[0].SuggestedTargetKey.ShouldBe("firstName");
        result[1].Samples.ShouldBe(["10000000146"]);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN-us", "en")]
    [InlineData("tr", "tr")]
    [InlineData("de", "tr")]
    public void ImportCultures_ResolveLangWithTurkishFallback(string lang, string expected) =>
        ImportCultures.Resolve(lang).TwoLetterISOLanguageName.ShouldBe(expected);

    [Fact]
    public void ImportCultures_UseRequestCultureWhenLangMissing()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            ImportCultures.Resolve(null).ShouldBe(ImportCultures.English);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static SpreadsheetRow Row(int rowNumber, params string[] cells) =>
        new(rowNumber, cells.Select(c => string.IsNullOrEmpty(c) ? ImportCell.Empty : new ImportCell(c)).ToList());

    private static LocalizedImportColumn Column(string key, params string[] headers) =>
        new(new ImportColumn(key, null, headers[0], ImportColumnDataType.Text, ImportColumnRequirement.Optional), headers[0], headers);
}
