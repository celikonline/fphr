using Sense.Modules.Payroll.Application.Declarations;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Pure unit tests for <see cref="AphbFileBuilder"/> (senseik-019 3b, APHB taslak dosya formatı).</summary>
public sealed class AphbFileBuilderTests
{
    [Fact]
    public void BuildContent_EmptyLines_StillProducesHeaderAndPeriodComment()
    {
        var content = AphbFileBuilder.BuildContent(2026, 3, []);

        content.ShouldContain("2026-03");
        content.ShouldContain(AphbFileBuilder.Header);
    }

    [Fact]
    public void BuildContent_OneLine_JoinsFieldsWithSemicolons()
    {
        var line = new AphbLine(
            NationalId: "12345678901", FullName: "Ayşe Yılmaz", SicilNo: "SIC-001", SgkWorkplaceCode: "ISY-01",
            PrimOdemeGunSayisi: 30, PrimeEsasKazanc: 200_000m, SigortaliPayi: 30_000m, IsverenPayi: 45_000m, IncentiveLawCode: "5510-5");

        var content = AphbFileBuilder.BuildContent(2026, 3, [line]);

        content.ShouldContain("12345678901;Ayşe Yılmaz;SIC-001;ISY-01;30;200000.00;30000.00;45000.00;5510-5");
    }

    [Fact]
    public void BuildContent_MissingIdentityFields_LeavesThemBlankRatherThanSkippingTheRow()
    {
        // senseik-009/010 tamamlanmadan TCKN/sicil/işyeri kodu boş kalabilir (bkz. handoff "Kalan İşler") - satır
        // yine üretilir.
        var line = new AphbLine(
            NationalId: string.Empty, FullName: "Mehmet Demir", SicilNo: string.Empty, SgkWorkplaceCode: string.Empty,
            PrimOdemeGunSayisi: 30, PrimeEsasKazanc: 30_000m, SigortaliPayi: 4_500m, IsverenPayi: 6_750m, IncentiveLawCode: null);

        var content = AphbFileBuilder.BuildContent(2026, 3, [line]);

        content.ShouldContain(";Mehmet Demir;;;30;30000.00;4500.00;6750.00;");
    }
}
