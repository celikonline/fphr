using Sense.Modules.Employee.Domain.Employees;

namespace Sense.Modules.Employee.Domain.Terminations;

/// <summary>One system-seed row, pre-tenant/id (<see cref="TerminationReason.Create"/> assigns those).</summary>
public sealed record TerminationReasonSeedRow(string Code, string Name, string? DefaultSgkExitCode, TerminationType? DefaultTerminationType, int SortOrder);

/// <summary>
/// System seed list for the termination reason catalog (11.3.03 E5a). SGK exit codes are plausible placeholders per
/// the spec's own instruction ("doğrulanmalı") - not a verified SGK işten ayrılış nedeni kod tablosu; verify before
/// payroll-grade/SGK-integration use. Reasons without an obvious statutory TerminationType (Ölüm, Askerlik, Emeklilik,
/// Evlilik, Deneme süresi içinde fesih, Grup içi nakil) leave DefaultTerminationType null - the wizard just doesn't
/// pre-fill a type for them and the user picks one.
/// </summary>
public static class TerminationReasonSeed
{
    public static IReadOnlyList<TerminationReasonSeedRow> SystemDefinitions() =>
    [
        Row("Death", "Ölüm", "21", null, 10),
        Row("MilitaryService", "Askerlik", "10", null, 20),
        Row("Resignation", "İstifa", "29", TerminationType.Resignation, 30),
        Row("EmployerValidCause", "İşveren feshi (geçerli neden)", "02", TerminationType.ValidCause_Art18, 40),
        Row("EmployeeJustCause", "İşçinin haklı feshi", "19", TerminationType.EmployeeJustCause_Art24, 50),
        Row("EmployerJustCause", "İşverenin haklı feshi", "15", TerminationType.EmployerJustCause_Art25, 60),
        Row("MutualRescission", "İkale", "22", TerminationType.MutualRescission, 70),
        Row("FixedTermExpiry", "Belirli süreli sözleşme sonu", "03", TerminationType.FixedTermExpiry, 80),
        Row("Retirement", "Emeklilik", "08", null, 90),
        Row("MarriageFemale", "Evlilik (kadın işçi)", "04", null, 100),
        Row("ProbationTermination", "Deneme süresi içinde fesih", "01", null, 110),
        Row("GroupTransfer", "Grup içi nakil", "27", null, 120),
        Row("Other", "Diğer", "13", TerminationType.Other, 130),
    ];

    private static TerminationReasonSeedRow Row(string code, string name, string? sgkExitCode, TerminationType? type, int sortOrder) =>
        new(code, name, sgkExitCode, type, sortOrder);
}
