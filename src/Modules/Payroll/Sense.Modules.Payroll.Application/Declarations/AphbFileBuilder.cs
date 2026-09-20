using System.Globalization;
using System.Text;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>One employee's APHB row (senseik-019 3b). TCKN/SicilNo/SgkWorkplaceCode may be empty - senseik-009
/// (sicil numarası) and senseik-010 (SGK işyeri sicil/meslek kodu) are not merged yet (bkz. handoff "Kalan İşler");
/// the row is still produced with the field left blank rather than skipped.</summary>
public sealed record AphbLine(
    string NationalId,
    string FullName,
    string SicilNo,
    string SgkWorkplaceCode,
    int PrimOdemeGunSayisi,
    decimal PrimeEsasKazanc,
    decimal SigortaliPayi,
    decimal IsverenPayi,
    string? IncentiveLawCode);

/// <summary>
/// SGK Aylık Prim ve Hizmet Belgesi (APHB/e-Bildirge) TASLAK dosya üreticisi (senseik-019 3b - "K-7: SGK e-Bildirge ve
/// Banka Ödeme Dosyası" backlog kaleminin karşılığı, bkz. docs/implementation/faz7-payroll-export-sozlesmesi.md).
/// BASİTLEŞTİRME - ÜRETİMDE KULLANILAMAZ: bu, SGK'nın resmi e-Bildirge XML/web servisi şemasını uygulamaz; yalnızca
/// APHB'nin kavramsal alanlarını (TCKN, sicil, prim gün sayısı, SPEK, sigortalı/işveren payı, teşvik kanun kodu)
/// noktalı virgülle ayrılmış bir taslak metin dosyasına döker, böylece format ve alan eşleştirmesi üzerinde
/// çalışılabilir ve golden-set testleriyle doğrulanabilir. Gerçek SGK entegrasyonu (resmi şema, e-imza, web servisi)
/// ayrı bir kart/faz gerektirir.
/// </summary>
public static class AphbFileBuilder
{
    public const string Header = "TCKN;AD_SOYAD;SICIL_NO;ISYERI_SICIL_NO;PRIM_GUN_SAYISI;SPEK;SIGORTALI_PAYI;ISVEREN_PAYI;TESVIK_KANUN_KODU";

    public static string BuildContent(int year, int month, IReadOnlyList<AphbLine> lines)
    {
        var sb = new StringBuilder();
        sb.Append("# APHB TASLAK (SGK resmi semasi degil) - Donem: ").Append(year.ToString("D4", CultureInfo.InvariantCulture))
            .Append('-').Append(month.ToString("D2", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(Header).Append('\n');
        foreach (var line in lines)
        {
            sb.Append(string.Join(
                ';',
                line.NationalId,
                line.FullName,
                line.SicilNo,
                line.SgkWorkplaceCode,
                line.PrimOdemeGunSayisi.ToString(CultureInfo.InvariantCulture),
                Format(line.PrimeEsasKazanc),
                Format(line.SigortaliPayi),
                Format(line.IsverenPayi),
                line.IncentiveLawCode ?? string.Empty));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
