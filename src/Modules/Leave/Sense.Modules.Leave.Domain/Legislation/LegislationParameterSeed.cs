namespace Sense.Modules.Leave.Domain.Legislation;

/// <summary>
/// Seed set (seed version 2026.05) - only values stated in 11.3.5 / 11.6 (both marked "doğrulanmalı" there).
/// EffectiveFrom of pre-7578 baselines is the enacting law's gazette date (4857: RG 10.06.2003/25134; Ek md.2 via
/// 6663 s.K.: RG 10.02.2016/29620) and must be legally verified before payroll-grade use (ADR 0008).
/// Keys mirror Sense.Shared.Contracts.Legislation.LegislationParameterKeys (Domain may not reference Contracts).
/// </summary>
public static class LegislationParameterSeed
{
    public const string Country = "TR";

    private static readonly DateOnly Law4857 = new(2003, 6, 10);
    private static readonly DateOnly Law6663 = new(2016, 2, 10);
    private static readonly DateOnly Law7578 = new(2026, 5, 1);
    private static readonly DateOnly Before7578 = new(2026, 4, 30);
    private static readonly DateOnly Regulation2017 = new(2017, 8, 25);
    private static readonly DateOnly Payroll2026 = new(2026, 1, 1);

    private const string Md53 = "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134)";
    private const string EkMd2 = "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620)";
    private const string EkMd2By7578 = "4857 s. İş Kanunu Ek md.2, 7578 s.K. ile değişik (RG 01.05.2026/33240)";
    private const string Md74 = "4857 s. İş Kanunu md.74 (RG 10.06.2003/25134)";
    private const string Md74By7578 = "4857 s. İş Kanunu md.74, 7578 s.K. ile değişik (RG 01.05.2026/33240)";
    private const string Md41 = "4857 s. İş Kanunu md.41 (RG 10.06.2003/25134)";

    /// <summary>
    /// Payroll (Faz 3 P1, 11.3.07-bordro.md): draft 2026 rates/brackets, explicitly unverified - "taslak, mali
    /// müşavirle doğrulanmalı" per the spec's own caveat on every payroll rate. Structurally correct and
    /// parameter-driven (nothing hardcoded in Sense.Modules.Payroll code); replace via a new dated row, never in place.
    /// </summary>
    private const string PayrollDraft2026 = "Taslak 2026 bordro parametresi - mali müşavirle doğrulanmalı (11.3.07-bordro.md)";

    /// <summary>2026 asgari ücret (brüt 33.030 TL / net 28.075,50 TL), 01.01.2026 - ÇSGB duyurusu (senseik QA teslim testi bulgu 7 ile doğrulandı).</summary>
    private const string MinWage2026Source = "2026 asgari ücret, Resmî Gazete (yürürlük 01.01.2026); kaynak: https://www.csgb.gov.tr/Media/gm2fekds/asgari-%C3%BCcret-2026.pdf";

    private const string SeveranceCeilingGenelge = "Hazine ve Maliye Bakanlığı Mali ve Sosyal Haklar Genelgesi (kıdem tazminatı tavanı, 6 ayda bir); kaynak: https://www.csgb.gov.tr/%C4%B1statistikler/calisma-hayati-%C4%B1statistikleri/kidem-tazminati-tavan-miktari/";

    public static IReadOnlyList<LegislationParameter> All { get; } =
    [
        P(1, "Leave.Annual.Tier1.Days", 14m, Law4857, null, Md53 + " - 1-5 yıl (5 dahil)"),
        P(2, "Leave.Annual.Tier2.AfterYears", 5m, Law4857, null, Md53 + " - 5 yıldan fazla"),
        P(3, "Leave.Annual.Tier2.Days", 20m, Law4857, null, Md53 + " - 5 yıldan fazla, 15 yıldan az"),
        P(4, "Leave.Annual.Tier3.AfterYears", 15m, Law4857, null, Md53 + " - 15 yıl (dahil) ve fazla"),
        P(5, "Leave.Annual.Tier3.Days", 26m, Law4857, null, Md53 + " - 15 yıl (dahil) ve fazla"),
        P(6, "Leave.Annual.YoungAgeMax", 18m, Law4857, null, Md53 + " - 18 ve daha küçük yaştaki işçiler"),
        P(7, "Leave.Annual.OldAgeMin", 50m, Law4857, null, Md53 + " - 50 ve daha yukarı yaştaki işçiler"),
        P(8, "Leave.Annual.AgeExceptionMinDays", 20m, Law4857, null, Md53 + " - yaş istisnası en az 20 gün"),
        P(9, "Leave.Annual.UndergroundExtraDays", 4m, Law4857, null, Md53 + " - yeraltı işlerinde +4 gün"),
        P(10, "Leave.Paternity.Days", 5m, Law6663, Before7578, EkMd2 + " - eşi doğum yapan işçiye 5 gün"),
        P(11, "Leave.Paternity.Days", 10m, Law7578, null, EkMd2By7578 + " - 10 gün (doğum tarihi referans)"),
        P(12, "Leave.Maternity.PreBirthWeeks", 8m, Law4857, null, Md74 + " - doğumdan önce 8 hafta"),
        P(13, "Leave.Maternity.PostBirthWeeks", 8m, Law4857, Before7578, Md74 + " - doğumdan sonra 8 hafta"),
        P(14, "Leave.Maternity.PostBirthWeeks", 16m, Law7578, null, Md74By7578 + " - doğumdan sonra 16 hafta"),
        P(15, "Leave.Marriage.Days", 3m, Law6663, null, EkMd2 + " - evlenme 3 gün"),
        P(16, "Leave.Adoption.Days", 3m, Law6663, null, EkMd2 + " - evlat edinme 3 gün"),
        P(17, "Leave.Bereavement.Days", 3m, Law6663, null, EkMd2 + " - anne/baba/eş/kardeş/çocuk ölümü 3 gün"),
        P(18, "Leave.DisabledChild.DaysPerYear", 10m, Law6663, null, EkMd2 + " - %70 engelli/süreğen hastalıklı çocuk, yılda 10 güne kadar"),
        P(19, "Leave.FosterCare.Days", 10m, Law7578, null, "7578 s.K. (RG 01.05.2026/33240) - koruyucu aile izni 10 gün ücretsiz"),
        P(20, "Leave.PaidMilitaryService.Days", 21m, new DateOnly(2019, 6, 26), null, "1111 s. Askerlik Kanunu geçici md.55 - bedelli askerlik temel eğitimi 21 gün (yürürlük tarihi doğrulanmalı)"),
        P(21, "Leave.JobSearch.MinHoursPerDay", 2m, Law4857, null, "4857 s. İş Kanunu md.27 - yeni iş arama izni günde en az 2 saat"),
        P(22, "Overtime.AnnualLimitHours", 270m, Law4857, null, Md41 + " - yıllık 270 saat"),
        P(23, "Overtime.CompTimeExpiryMonths", 6m, Law4857, null, Md41 + " - serbest zaman 6 ay içinde kullanılır"),
        P(24, "Overtime.ConsentRevocationNoticeDays", 30m, Regulation2017, null, "Fazla Çalışma Yönetmeliği md.9 (RG 25.08.2017/30165) - onay 30 gün önceden yazılı bildirimle geri alınır"),

        // Payroll (Faz 3 P1) - see PayrollDraft2026 doc comment above.
        P(25, "Payroll.MinimumWage.Gross", 33030.00m, Payroll2026, null, MinWage2026Source + " - aylık brüt asgari ücret"),
        P(26, "Payroll.Sgk.Floor", 33030.00m, Payroll2026, null, "5510 s. Kanun md.82; " + MinWage2026Source + " - SGK prim matrahı alt sınırı (asgari ücrete eşit)"),
        P(27, "Payroll.Sgk.Ceiling", 247725.00m, Payroll2026, null, "5510 s. Kanun md.82; " + MinWage2026Source + " - SGK prim matrahı üst sınırı (asgari ücretin 7,5 katı = 247.725 TL)"),
        P(28, "Payroll.Sgk.EmployeeRate", 0.14m, Payroll2026, null, "5510 s. Kanun md.81 - SGK işçi hissesi (malullük/yaşlılık/ölüm + genel sağlık) %14"),
        P(29, "Payroll.Unemployment.EmployeeRate", 0.01m, Payroll2026, null, "4447 s. İşsizlik Sigortası Kanunu md.49 - işsizlik sigortası işçi payı %1"),
        P(30, "Payroll.Sgk.EmployerRate", 0.205m, Payroll2026, null, "5510 s. Kanun md.81 - SGK işveren hissesi (indirimsiz) %20,5"),
        P(31, "Payroll.Unemployment.EmployerRate", 0.02m, Payroll2026, null, "4447 s. İşsizlik Sigortası Kanunu md.49 - işsizlik sigortası işveren payı %2"),
        P(32, "Payroll.Sgk.EmployerDiscountRate", 0.05m, Payroll2026, null, "5510 s. Kanun md.81 - 5 puanlık SGK işveren teşviki oranı (uygunluk hesabı henüz uygulanmıyor, TODO P2+)"),
        P(33, "Payroll.StampTax.Rate", 0.00759m, Payroll2026, null, "488 s. Damga Vergisi Kanunu (1) sayılı tablo IV/1-b - ücretlerde binde 7,59"),
        P(34, "Payroll.Meal.IncomeTax.DailyExemption", 240.00m, Payroll2026, null, PayrollDraft2026 + " - nakit/yalnız yemek kartı günlük gelir vergisi istisnası"),
        P(35, "Payroll.Meal.Sgk.DailyExemptionRate", 0.2365m, Payroll2026, null, PayrollDraft2026 + " - SGK Genelgesi yaklaşımı: günlük brüt asgari ücretin %23,65'i SGK'dan istisna"),
        PJson(36, "Payroll.IncomeTax.Brackets", Payroll2026, null,
            """[{"upperBound":190000,"rate":0.15},{"upperBound":400000,"rate":0.20},{"upperBound":1500000,"rate":0.27},{"upperBound":5300000,"rate":0.35},{"upperBound":null,"rate":0.40}]""",
            "193 s. GVK md.103 - 2026 ücret gelirleri tarifesi (%15/20/27/35/40; 190.000/400.000/1.500.000/5.300.000), 332 Sıra No.lu Gelir Vergisi Genel Tebliği; kaynak: https://www.memurlar.net/haber/1155986/2026-gelir-vergisi-dilimlerini-gosteren-teblig-r-g-de-yayimlandi.html"),

        // Employee job-security scope check (Faz 1a E5a, 11.3.03 "İşten ayrılış sihirbazı"): approximated with total
        // active tenant headcount (no iş kolu/business-line grouping in the codebase yet - see JobSecurityEvaluator).
        P(37, "JobSecurity.MinHeadcount", 30m, Law4857, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: 30 ve üzeri işçi çalıştıran işyeri (yaklaşık, iş kolu gruplaması hariç)"),
        P(38, "JobSecurity.MinSeniorityMonths", 6m, Law4857, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: en az 6 aylık kıdem"),

        // Payroll (Faz 3 P2, 11.3.07-bordro.md "Yemek varyant kuralları") - see PayrollDraft2026 doc comment above.
        P(39, "Payroll.Meal.MarketCard.ExemptionApplies", 0m, Payroll2026, null, PayrollDraft2026 + " - yemek+market kartı varyantına Cash/MealCardOnly istisnası uygulanmıyor (0 = hayır), tamamı matraha girer"),

        // Severance/notice estimate (senseik-011, F-06 "İşten ayrılış sihirbazı"): kıdem tazminatı tavanı 6 ayda bir
        // güncellenir (taslak değer - mali müşavirle doğrulanmalı, PayrollDraft2026 ile aynı çekince). İhbar tablosu
        // İş K. m.17'nin kıdem/hafta eşiklerini gün cinsinden kodlar (183/548/1095 gün ~ 6 ay/1,5 yıl/3 yıl).
        P(40, "Severance.CeilingGross", 64948.77m, Payroll2026, new DateOnly(2026, 6, 30), SeveranceCeilingGenelge + " - 01.01.2026-30.06.2026 kıdem tazminatı tavanı 64.948,77 TL (bir hizmet yılı için azami brüt tutar, İş K. m.14)"),
        P(46, "Severance.CeilingGross", 73729.87m, new DateOnly(2026, 7, 1), null, SeveranceCeilingGenelge + " - 01.07.2026-31.12.2026 kıdem tazminatı tavanı 73.729,87 TL"),
        PJson(41, "Notice.WeeksBySeniorityTiers", Payroll2026, null,
            """[{"upperBoundDays":183,"weeks":2},{"upperBoundDays":548,"weeks":4},{"upperBoundDays":1095,"weeks":6},{"upperBoundDays":null,"weeks":8}]""",
            "4857 s. İş Kanunu md.17 - ihbar önel tablosu (6 aya kadar 2 hafta, 6 ay-1,5 yıl 4 hafta, 1,5-3 yıl 6 hafta, 3 yıldan fazla 8 hafta)"),

        // senseik-036 (izin türü seed düzeltme paketi): süt izni saati (4857 md.74/son; 7578 s.K. bu hükmü değiştirmedi).
        P(42, "Leave.Breastfeeding.HoursPerDay", 1.5m, Law4857, null, Md74 + "/son - 1 yaşından küçük çocuk için günde toplam süt izni (saat)"),

        // Payroll (senseik-019 2b, GVK md.31 engelli çalışan gelir vergisi matrah indirimi) - taslak aylık tutarlar,
        // her yıl GİB tarafından güncellenir; PayrollDraft2026 doc comment'teki gibi mali müşavirle doğrulanmalı.
        P(43, "Payroll.Disability.MonthlyTaxReduction.Degree1", 6900.00m, Payroll2026, null, "193 s. GVK md.31 - I. derece (en ağır) engellilik indirimi, " + PayrollDraft2026),
        P(44, "Payroll.Disability.MonthlyTaxReduction.Degree2", 4000.00m, Payroll2026, null, "193 s. GVK md.31 - II. derece engellilik indirimi, " + PayrollDraft2026),
        P(45, "Payroll.Disability.MonthlyTaxReduction.Degree3", 1700.00m, Payroll2026, null, "193 s. GVK md.31 - III. derece engellilik indirimi, " + PayrollDraft2026),
    ];

    private static LegislationParameter P(int n, string key, decimal value, DateOnly from, DateOnly? to, string source) =>
        new(new Guid($"00000008-0000-0000-0000-{n:D12}"), key, Country, value, null, from, to, source);

    private static LegislationParameter PJson(int n, string key, DateOnly from, DateOnly? to, string json, string source) =>
        new(new Guid($"00000008-0000-0000-0000-{n:D12}"), key, Country, null, json, from, to, source);
}
