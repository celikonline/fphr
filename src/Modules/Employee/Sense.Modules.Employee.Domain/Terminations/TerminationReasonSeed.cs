using Sense.Modules.Employee.Domain.Employees;

namespace Sense.Modules.Employee.Domain.Terminations;

/// <summary>One system-seed row, pre-tenant/id (<see cref="TerminationReason.Create"/> assigns those).</summary>
public sealed record TerminationReasonSeedRow(string Code, string Name, string? DefaultSgkExitCode, TerminationType? DefaultTerminationType, int SortOrder);

/// <summary>
/// System seed list for the termination reason catalog (11.3.03 E5a). SGK exit codes are verified against SGK's
/// official "İşten Ayrılış Nedenleri" reference table (senseik-006/F-01 - docs/handoffs/sgk-exit-codes.md §1 has the
/// cross-checked sources). Reasons without an obvious statutory TerminationType (Ölüm, Askerlik, Emeklilik, Evlilik,
/// Deneme süresi içinde fesih, Grup içi nakil) leave DefaultTerminationType null - the wizard just doesn't pre-fill a
/// type for them and the user picks one. "İşçinin haklı feshi"/"İşverenin haklı feshi" leave DefaultSgkExitCode null
/// on purpose: each covers a family of SGK codes (23/24/25 and 26/27/28 respectively, see
/// <see cref="SgkExitCodeRules"/>) with no single safe default - the wizard forces an explicit sub-code pick instead
/// of silently pre-filling a possibly-wrong one (handoff §5 open question 5).
/// </summary>
public static class TerminationReasonSeed
{
    public static IReadOnlyList<TerminationReasonSeedRow> SystemDefinitions() =>
    [
        Row("Death", "Ölüm", "10", null, 10),
        Row("MilitaryService", "Askerlik", "12", null, 20),
        Row("Resignation", "İstifa", "03", TerminationType.Resignation, 30),
        Row("EmployerValidCause", "İşveren feshi (geçerli neden)", "04", TerminationType.ValidCause_Art18, 40),
        Row("EmployeeJustCause", "İşçinin haklı feshi", null, TerminationType.EmployeeJustCause_Art24, 50),
        Row("EmployerJustCause", "İşverenin haklı feshi", null, TerminationType.EmployerJustCause_Art25, 60),
        Row("MutualRescission", "İkale", "22", TerminationType.MutualRescission, 70),
        Row("FixedTermExpiry", "Belirli süreli sözleşme sonu", "05", TerminationType.FixedTermExpiry, 80),
        Row("Retirement", "Emeklilik", "08", null, 90),
        Row("MarriageFemale", "Evlilik (kadın işçi)", "13", null, 100),
        Row("ProbationTermination", "Deneme süresi içinde fesih", "01", null, 110),
        Row("GroupTransfer", "Grup içi nakil", "16", null, 120),
        Row("Other", "Diğer", "22", TerminationType.Other, 130),
    ];

    private static TerminationReasonSeedRow Row(string code, string name, string? sgkExitCode, TerminationType? type, int sortOrder) =>
        new(code, name, sgkExitCode, type, sortOrder);
}
