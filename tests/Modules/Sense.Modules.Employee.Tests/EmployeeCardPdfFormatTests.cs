using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeCardPdfFormatTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    [Fact]
    public void InPeriod_OpenEndedRecordStartingInsidePeriod_IsIncluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2026, 3, 1), null, From, To).ShouldBeTrue();

    [Fact]
    public void InPeriod_RecordEndingBeforePeriod_IsExcluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2025, 3, 1), new DateOnly(2025, 12, 31), From, To).ShouldBeFalse();

    [Fact]
    public void InPeriod_RecordStartingAfterPeriod_IsExcluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2026, 7, 1), null, From, To).ShouldBeFalse();

    [Fact]
    public void InPeriod_RecordSpanningPeriodStart_IsIncluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2025, 12, 20), new DateOnly(2026, 1, 5), From, To).ShouldBeTrue();

    [Fact]
    public void InPeriod_NoBounds_IsIncluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2001, 1, 1), null, null, null).ShouldBeTrue();

    [Fact]
    public void Table_OverLimit_TruncatesAndAddsNote()
    {
        var rows = Enumerable.Range(0, EmployeeCardPdfFormat.MaxRowsPerTable + 3)
            .Select(i => (IReadOnlyList<string>)[i.ToString()]);

        var blocks = EmployeeCardPdfFormat.Table("İzinler", ["No"], rows).ToList();

        blocks[0].ShouldBeOfType<SimplePdfTable>().Rows.Count.ShouldBe(EmployeeCardPdfFormat.MaxRowsPerTable);
        blocks[1].ShouldBeOfType<SimplePdfParagraph>().Text.ShouldBe("3 kayıt daha var; dönemi daraltın.");
    }

    [Fact]
    public void Table_NoRows_WritesEmptyLine() =>
        EmployeeCardPdfFormat.Table("İzinler", ["No"], []).ShouldHaveSingleItem()
            .ShouldBeOfType<SimplePdfParagraph>().Text.ShouldBe("İzinler: kayıt yok.");

    [Fact]
    public void Part_Forbidden_IsNull() =>
        EmployeeCardPdfSectionResult.Part(Result.Failure<int>(Error.Forbidden("auth.forbidden")), _ => []).ShouldBeNull();

    [Fact]
    public void Part_NotFound_IsNull() =>
        EmployeeCardPdfSectionResult.Part(Result.Failure<int>(Error.NotFound("x")), _ => []).ShouldBeNull();

    [Fact]
    public void Part_Failure_Throws() =>
        Should.Throw<InvalidOperationException>(() =>
            EmployeeCardPdfSectionResult.Part(Result.Failure<int>(Error.Failure("x")), _ => []));

    [Fact]
    public void Combine_AllPartsNull_IsUnavailable() =>
        EmployeeCardPdfSectionResult.Combine(null, null).IsAvailable.ShouldBeFalse();

    [Fact]
    public void Combine_OnePartAvailable_KeepsItsBlocks() =>
        EmployeeCardPdfSectionResult.Combine(null, [new SimplePdfParagraph("a")]).Blocks!.Count.ShouldBe(1);
}
