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

    private const string Md53 = "4857 s. İş Kanunu md.53 (RG 10.06.2003/25134)";
    private const string EkMd2 = "4857 s. İş Kanunu Ek md.2 (6663 s.K., RG 10.02.2016/29620)";
    private const string EkMd2By7578 = "4857 s. İş Kanunu Ek md.2, 7578 s.K. ile değişik (RG 01.05.2026/33240)";
    private const string Md74 = "4857 s. İş Kanunu md.74 (RG 10.06.2003/25134)";
    private const string Md74By7578 = "4857 s. İş Kanunu md.74, 7578 s.K. ile değişik (RG 01.05.2026/33240)";
    private const string Md41 = "4857 s. İş Kanunu md.41 (RG 10.06.2003/25134)";

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

        // Employee job-security scope check (Faz 1a E5a, 11.3.03 "İşten ayrılış sihirbazı"): approximated with total
        // active tenant headcount (no iş kolu/business-line grouping in the codebase yet - see JobSecurityEvaluator).
        P(37, "JobSecurity.MinHeadcount", 30m, Law4857, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: 30 ve üzeri işçi çalıştıran işyeri (yaklaşık, iş kolu gruplaması hariç)"),
        P(38, "JobSecurity.MinSeniorityMonths", 6m, Law4857, null, "4857 s. İş Kanunu md.18 (RG 10.06.2003/25134) - iş güvencesi kapsamı: en az 6 aylık kıdem"),
    ];

    private static LegislationParameter P(int n, string key, decimal value, DateOnly from, DateOnly? to, string source) =>
        new(new Guid($"00000008-0000-0000-0000-{n:D12}"), key, Country, value, null, from, to, source);
}
