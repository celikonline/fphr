namespace Sense.Modules.Payroll.Domain.Incentives;

/// <summary>
/// A starting point for a tenant-specific incentive definition. Draft templates contain no calculation parameters and need
/// SMMM review before approval; even pre-approved templates do not determine workplace or employee eligibility.
/// </summary>
public sealed record IncentiveTemplate(
    string TemplateKey,
    string Code,
    string Name,
    string? LawReference,
    IncentiveKind Kind,
    string? DeclarationCode,
    IncentiveParameters Parameters,
    bool PreApproved,
    string? ApprovalNote,
    int? MaxMonths = null,
    DateOnly? StartsOn = null,
    DateOnly? EndsOn = null)
{
    public IncentiveDefinitionData ToData(DateOnly validFrom) =>
        new(Name, LawReference, DeclarationCode, Kind, Parameters, MaxMonths, StartsOn ?? validFrom, EndsOn);
}

public static class IncentiveTemplates
{
    /// <summary>Dated general-sector discount parameter; rates are resolved for the payroll period.</summary>
    public const string EmployerDiscountRateKey = "Payroll.Sgk.EmployerDiscountRate";
    public const string EmployerSgkRateKey = "Payroll.Sgk.EmployerRate";
    public const string BeneficiaryEmployerSupportRateKey = "Payroll.Sgk.UnemploymentBeneficiaryEmployerSupportRate";

    public const string Tr5510Md81 = "TR_5510_MD81";
    public const string Tr5510Manufacturing = "TR_5510_MANUFACTURING";
    public const string Tr6111 = "TR_6111";
    public const string Tr4447Md50 = "TR_4447_MD50";
    public const string Tr7103Md17 = "TR_7103_17103";
    public const string Tr7103Md27 = "TR_7103_27103";
    public const string Tr5746Gv = "TR_5746_GV";
    public const string Tr5746Sgk = "TR_5746_SGK";
    public const string Tr4691Gv = "TR_4691_GV";
    public const string Tr4691Sgk = "TR_4691_SGK";
    public const string Tr4857Md30 = "TR_4857_MD30";

    public const string Sgk5510DeclarationCode = "05510";
    public const string SystemApprovalNote = "Sistem: dönemsel mevzuat parametresine bağlı genel sektör indirimi";

    public static IReadOnlyList<IncentiveTemplate> All { get; } =
    [
        new IncentiveTemplate(
            Tr5510Md81,
            "SGK_5510_MD81",
            "5510 md.81 işveren SGK indirimi",
            "5510 sayılı Kanun md.81",
            IncentiveKind.EmployerSgkRateDiscount,
            Sgk5510DeclarationCode,
            new IncentiveParameters { Rate = IncentiveRate.Key(EmployerDiscountRateKey) },
            PreApproved: true,
            SystemApprovalNote),
        new IncentiveTemplate(
            Tr5510Manufacturing, "SGK_5510_IMALAT", "5510 imalat sektörü 5 puan indirimi", "5510 md.81/ı ve geçici md.108",
            IncentiveKind.EmployerSgkRateDiscount, Sgk5510DeclarationCode,
            new IncentiveParameters { Rate = IncentiveRate.Literal(0.05m) }, false, null,
            StartsOn: new DateOnly(2025, 2, 1), EndsOn: new DateOnly(2026, 12, 31)),
        new IncentiveTemplate(
            Tr6111, "SGK_6111", "6111 genç, kadın ve mesleki belge istihdam teşviki", "6111 / 4447 geç. md.10",
            IncentiveKind.EmployerShareCoverage, "06111",
            new IncentiveParameters { Rate = IncentiveRate.Literal(1m), CoverageBase = IncentiveCoverageBase.ActualSgkBase,
                EmployerSgkRate = IncentiveRate.Key(EmployerSgkRateKey), CoversEmployerSgk = true }, false, null, MaxMonths: 54),
        new IncentiveTemplate(
            Tr4447Md50, "SGK_15921", "4447 md.50 işsizlik ödeneği alanların istihdamı", "4447 sayılı Kanun md.50/5",
            IncentiveKind.EmployerShareCoverage, "15921",
            new IncentiveParameters { Rate = IncentiveRate.Literal(1m), CoverageBase = IncentiveCoverageBase.MinimumWage,
                CapMultiplier = 1m, EmployerSgkRate = IncentiveRate.Key(BeneficiaryEmployerSupportRateKey),
                CoversEmployerSgk = true, CoversEmployeeSgk = true }, false, null, MaxMonths: 10),
        Historical7103(Tr7103Md17, "SGK_17103", "7103 imalat ve bilişim istihdam teşviki (2018-2022)", "17103", IncentiveCoverageBase.ActualSgkBase),
        Historical7103(Tr7103Md27, "SGK_27103", "7103 diğer sektörler istihdam teşviki (2018-2022)", "27103", IncentiveCoverageBase.MinimumWage),
        Draft(Tr5746Gv, "ARGE_5746_GV_TASLAK", "5746 Ar-Ge/Tasarım gelir vergisi stopaj teşviki (taslak)", "5746 sayılı Kanun md.3", IncentiveKind.IncomeTaxWithholdingRelief),
        Draft(Tr5746Sgk, "ARGE_5746_SGK_TASLAK", "5746 Ar-Ge/Tasarım SGK işveren hissesi desteği (taslak)", "5746 sayılı Kanun md.3", IncentiveKind.EmployerShareCoverage),
        Draft(Tr4691Gv, "TEKNOKENT_4691_GV_TASLAK", "4691 Teknoloji Geliştirme Bölgesi gelir vergisi stopaj teşviki (taslak)", "4691 sayılı Kanun geçici md.2", IncentiveKind.IncomeTaxWithholdingRelief),
        Draft(Tr4691Sgk, "TEKNOKENT_4691_SGK_TASLAK", "4691 Teknoloji Geliştirme Bölgesi SGK işveren hissesi desteği (taslak)", "4691 sayılı Kanun geçici md.2", IncentiveKind.EmployerShareCoverage),
        Draft(Tr4857Md30, "SGK_4857_MD30_TASLAK", "4857 md.30 engelli istihdamı işveren SGK hissesi teşviki (taslak)", "4857 sayılı Kanun md.30", IncentiveKind.EmployerShareCoverage),
    ];

    public static IncentiveTemplate? Find(string? templateKey) =>
        All.FirstOrDefault(t => string.Equals(t.TemplateKey, templateKey, StringComparison.Ordinal));

    private static IncentiveTemplate Draft(string key, string code, string name, string lawReference, IncentiveKind kind) =>
        new(key, code, name, lawReference, kind, null, IncentiveParameters.Empty, PreApproved: false, null);

    // SGK 2018/22: 17103 caps the total support at the gross minimum wage; 27103 caps the premium base instead.
    // Both branches cover SGK and unemployment shares, and cannot extend beyond December 2022.
    private static IncentiveTemplate Historical7103(string key, string code, string name, string declarationCode, IncentiveCoverageBase coverageBase) =>
        new(key, code, name, "7103 / 4447 geç. md.19 (2018/22 sayılı SGK Genelgesi)", IncentiveKind.EmployerShareCoverage, declarationCode,
            new IncentiveParameters
            {
                Rate = IncentiveRate.Literal(1m), CoverageBase = coverageBase,
                CapMultiplier = coverageBase == IncentiveCoverageBase.MinimumWage ? 1m : null,
                MaxSupportMultiplier = coverageBase == IncentiveCoverageBase.ActualSgkBase ? 1m : null,
                EmployerSgkRate = IncentiveRate.Literal(0.205m), CoversEmployerSgk = true, CoversEmployerUnemployment = true,
                CoversEmployeeSgk = true, CoversEmployeeUnemployment = true,
            }, false, null, MaxMonths: 18, StartsOn: new DateOnly(2018, 1, 1), EndsOn: new DateOnly(2022, 12, 31));
}
