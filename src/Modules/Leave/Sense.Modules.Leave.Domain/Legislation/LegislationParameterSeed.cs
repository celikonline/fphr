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
    /// Payroll values below were checked against SGK, GİB, ÇSGB and Resmî Gazete primary sources on 2026-10-14.
    /// Incentive eligibility remains case-specific and is not certified by a seeded rate.
    /// </summary>
    private const string PayrollVerified2026 = "2026-10-14 resmi kaynak kontrolü";

    /// <summary>2026 asgari ücret (brüt 33.030 TL / net 28.075,50 TL), 01.01.2026 - ÇSGB duyurusu (senseik QA teslim testi bulgu 7 ile doğrulandı).</summary>
    private const string MinWage2026Source = "2026 asgari ücret, Resmî Gazete (yürürlük 01.01.2026); kaynak: https://www.csgb.gov.tr/Media/gm2fekds/asgari-%C3%BCcret-2026.pdf";
    private const string Sgk2026RatesSource = "5510 md.81, 7566 s.K. (01.01.2026); SGK işveren prim oranları: https://www.sgk.gov.tr/Content/Post/c7812ea8-5087-413f-aeb5-d3c1d153e11a/Isveren-Prim-Oranlari-2026-01-13-04-52-38";
    private const string Sgk2026IncentivesSource = "SGK 2026/1 Genelgesi; https://sgk.gov.tr/Content/Post/c52f99f0-da6c-4029-9b9a-a0d6f96a6a42/Guncel-Sigorta-Primi-Tesvik-Destek-ve-Indirimleri-2026-08-04-10-29-55";

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

        // Payroll rates are parameterized and linked to official sources; incentive eligibility remains employer-specific.
        P(25, "Payroll.MinimumWage.Gross", 33030.00m, Payroll2026, null, MinWage2026Source + " - aylık brüt asgari ücret"),
        P(26, "Payroll.Sgk.Floor", 33030.00m, Payroll2026, null, "5510 s. Kanun md.82; " + MinWage2026Source + " - SGK prim matrahı alt sınırı (asgari ücrete eşit)"),
        P(27, "Payroll.Sgk.Ceiling", 297270.00m, Payroll2026, null, "5510 md.82, 7566 s.K. - 2026 SGK tavanı asgari ücretin 9 katı; " + MinWage2026Source),
        P(28, "Payroll.Sgk.EmployeeRate", 0.14m, Payroll2026, null, "5510 s. Kanun md.81 - SGK işçi hissesi (malullük/yaşlılık/ölüm + genel sağlık) %14"),
        P(29, "Payroll.Unemployment.EmployeeRate", 0.01m, Payroll2026, null, "4447 s. İşsizlik Sigortası Kanunu md.49 - işsizlik sigortası işçi payı %1"),
        P(30, "Payroll.Sgk.EmployerRate", 0.2175m, Payroll2026, null, Sgk2026RatesSource + " - indirimsiz işveren SGK oranı %21,75 (işsizlik hariç)"),
        P(31, "Payroll.Unemployment.EmployerRate", 0.02m, Payroll2026, null, "4447 s. İşsizlik Sigortası Kanunu md.49 - işsizlik sigortası işveren payı %2"),
        P(32, "Payroll.Sgk.EmployerDiscountRate", 0.02m, Payroll2026, null, Sgk2026IncentivesSource + " - genel sektör 2 puan indirimi; imalat 5 puan modeli ayrı atanır"),
        P(33, "Payroll.StampTax.Rate", 0.00759m, Payroll2026, null, "488 s. Damga Vergisi Kanunu (1) sayılı tablo IV/1-b - ücretlerde binde 7,59"),
        P(34, "Payroll.Meal.IncomeTax.DailyExemption", 300.00m, Payroll2026, null, "193 s. GVK md.23/8 ve 2026 gelir vergisi tarifesi; GİB: https://usak.gib.gov.tr/vergi-konulari/1_bireysel/11_ucret_geliri/11 - nakit/yemek kartı günlük GV istisnası"),
        P(35, "Payroll.Meal.Sgk.DailyExemption", 158.00m, Payroll2026, null, "SGK 2026 prime esas kazanç miktarları: https://www.sgk.gov.tr/Content/Post/2e0c9e1a-2cfe-4456-af10-49d3de0c58ba/Prime-Esas-Kazanc-Miktarlari-2026-01-14-10-35-39 - fiilen çalışılan gün başına SGK yemek istisnası 158 TL"),
        PJson(36, "Payroll.IncomeTax.Brackets", Payroll2026, null,
            """[{"upperBound":190000,"rate":0.15},{"upperBound":400000,"rate":0.20},{"upperBound":1500000,"rate":0.27},{"upperBound":5300000,"rate":0.35},{"upperBound":null,"rate":0.40}]""",
            "193 s. GVK md.103 - 2026 ücret gelirleri tarifesi (%15/20/27/35/40; 190.000/400.000/1.500.000/5.300.000), 332 Sıra No.lu Gelir Vergisi Genel Tebliği; GİB: https://gib.gov.tr/mevzuat/kanun/433/madde/6937"),

        // Employee job-security scope check (Faz 1a E5a, 11.3.03 "İşten ayrılış sihirbazı"): approximated with total
        // active tenant headcount (no iş kolu/business-line grouping in the codebase yet - see JobSecurityEvaluator).
        P(37, "JobSecurity.MinHeadcount", 30m, Law4857, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: 30 ve üzeri işçi çalıştıran işyeri (yaklaşık, iş kolu gruplaması hariç)"),
        P(38, "JobSecurity.MinSeniorityMonths", 6m, Law4857, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: en az 6 aylık kıdem"),

        // Payroll (Faz 3 P2, 11.3.07-bordro.md "Yemek varyant kuralları").
        P(39, "Payroll.Meal.MarketCard.ExemptionApplies", 0m, Payroll2026, null, PayrollVerified2026 + " - market harcamasına açık kartın yemek istisnası kapsamına girdiği kabul edilmez; kart türü/harcama kısıtı doğrulanmalı"),

        // Severance/notice estimate (senseik-011, F-06 "İşten ayrılış sihirbazı"): kıdem tazminatı tavanı 6 ayda bir
        // güncellenir. İhbar tablosu
        // İş K. m.17'nin kıdem/hafta eşiklerini gün cinsinden kodlar (183/548/1095 gün ~ 6 ay/1,5 yıl/3 yıl).
        P(40, "Severance.CeilingGross", 64948.77m, Payroll2026, new DateOnly(2026, 6, 30), SeveranceCeilingGenelge + " - 01.01.2026-30.06.2026 kıdem tazminatı tavanı 64.948,77 TL (bir hizmet yılı için azami brüt tutar, İş K. m.14)"),
        P(46, "Severance.CeilingGross", 73729.87m, new DateOnly(2026, 7, 1), null, SeveranceCeilingGenelge + " - 01.07.2026-31.12.2026 kıdem tazminatı tavanı 73.729,87 TL"),
        PJson(41, "Notice.WeeksBySeniorityTiers", Payroll2026, null,
            """[{"upperBoundDays":183,"weeks":2},{"upperBoundDays":548,"weeks":4},{"upperBoundDays":1095,"weeks":6},{"upperBoundDays":null,"weeks":8}]""",
            "4857 s. İş Kanunu md.17 - ihbar önel tablosu (6 aya kadar 2 hafta, 6 ay-1,5 yıl 4 hafta, 1,5-3 yıl 6 hafta, 3 yıldan fazla 8 hafta)"),

        // senseik-036 (izin türü seed düzeltme paketi): süt izni saati (4857 md.74/son; 7578 s.K. bu hükmü değiştirmedi).
        P(42, "Leave.Breastfeeding.HoursPerDay", 1.5m, Law4857, null, Md74 + "/son - 1 yaşından küçük çocuk için günde toplam süt izni (saat)"),

        // Payroll (senseik-019 2b, GVK md.31 engelli çalışan gelir vergisi matrah indirimi) - taslak aylık tutarlar,
        // her yıl GİB tarafından güncellenir.
        P(43, "Payroll.Disability.MonthlyTaxReduction.Degree1", 12000.00m, Payroll2026, null, "193 s. GVK md.31; 332 Seri No.lu Tebliğ - GİB 2026 engellilik indirimi: https://gib.gov.tr/vergi-konulari/1_bireysel/8_engelliler_icin_vergi_avantajlari/8"),
        P(44, "Payroll.Disability.MonthlyTaxReduction.Degree2", 7000.00m, Payroll2026, null, "193 s. GVK md.31; 332 Seri No.lu Tebliğ - GİB 2026 engellilik indirimi: https://gib.gov.tr/vergi-konulari/1_bireysel/8_engelliler_icin_vergi_avantajlari/8"),
        P(45, "Payroll.Disability.MonthlyTaxReduction.Degree3", 3000.00m, Payroll2026, null, "193 s. GVK md.31; 332 Seri No.lu Tebliğ - GİB 2026 engellilik indirimi: https://gib.gov.tr/vergi-konulari/1_bireysel/8_engelliler_icin_vergi_avantajlari/8"),
        P(47, "Payroll.Sgk.UnemploymentBeneficiaryEmployerSupportRate", 0.205m, Payroll2026, null, "4447 md.50/5 - uzun vadeli ve GSS işveren payları (%19,5) + kısa vadeli %1; " + Sgk2026RatesSource),
    ];

    private static LegislationParameter P(int n, string key, decimal value, DateOnly from, DateOnly? to, string source) =>
        new(new Guid($"00000008-0000-0000-0000-{n:D12}"), key, Country, value, null, from, to, source);

    private static LegislationParameter PJson(int n, string key, DateOnly from, DateOnly? to, string json, string source) =>
        new(new Guid($"00000008-0000-0000-0000-{n:D12}"), key, Country, null, json, from, to, source);
}
