namespace Sense.Modules.Payroll.Domain.Incentives;

/// <summary>
/// One entry of the in-code template catalog ("Şablondan ekle"). Templates carry no rates of their own: the 5510
/// template only references the existing legislation key (and is pre-approved because it reproduces today's behaviour);
/// every other template is an empty, unapproved draft the tenant's SMMM must fill in.
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
    string? ApprovalNote)
{
    public IncentiveDefinitionData ToData(DateOnly validFrom) =>
        new(Name, LawReference, DeclarationCode, Kind, Parameters, null, validFrom, null);
}

public static class IncentiveTemplates
{
    /// <summary>The existing legislation parameter behind today's flat 5 point employer discount (0.05).</summary>
    public const string EmployerDiscountRateKey = "Payroll.Sgk.EmployerDiscountRate";

    public const string Tr5510Md81 = "TR_5510_MD81";
    public const string Tr6111 = "TR_6111";
    public const string Tr7103Md17 = "TR_7103_17103";
    public const string Tr7103Md27 = "TR_7103_27103";
    public const string Tr5746Gv = "TR_5746_GV";

    public const string Sgk5510DeclarationCode = "05510";
    public const string SystemApprovalNote = "Sistem: mevcut 5 puan davranışı";

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
        Draft(Tr6111, "SGK_6111_TASLAK", "6111 teşviki (taslak)", "6111 / 4447 geç. md.10", IncentiveKind.EmployerShareCoverage),
        Draft(Tr7103Md17, "SGK_17103_TASLAK", "17103 teşviki (taslak)", "7103 sayılı Kanun (17103)", IncentiveKind.EmployerShareCoverage),
        Draft(Tr7103Md27, "SGK_27103_TASLAK", "27103 teşviki (taslak)", "7103 sayılı Kanun (27103)", IncentiveKind.EmployerShareCoverage),
        Draft(Tr5746Gv, "ARGE_5746_GV_TASLAK", "5746 Ar-Ge gelir vergisi teşviki (taslak)", "5746 sayılı Kanun", IncentiveKind.IncomeTaxWithholdingRelief),
    ];

    public static IncentiveTemplate? Find(string? templateKey) =>
        All.FirstOrDefault(t => string.Equals(t.TemplateKey, templateKey, StringComparison.Ordinal));

    private static IncentiveTemplate Draft(string key, string code, string name, string lawReference, IncentiveKind kind) =>
        new(key, code, name, lawReference, kind, null, IncentiveParameters.Empty, PreApproved: false, null);
}
