using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// Persisted "Hesap turnusolu" document for one <see cref="Payslip"/> run (11.3.7). <see cref="StepsJson"/> holds the
/// serialized <c>Calculation.PayrollCalculationResult.Steps</c> (the Application layer serializes; Domain stays free
/// of a JSON library dependency) - one row per calculation run, so re-runs keep prior versions for the "sürümler
/// arası fark" comparison (not built yet - P3).
/// </summary>
public sealed class PayslipCalculationTrace : TenantEntity<Guid>
{
    private PayslipCalculationTrace()
    {
    }

    private PayslipCalculationTrace(
        Guid id, Guid tenantId, Guid payslipId, Guid runId, int version, string stepsJson, Guid parameterSnapshotId, DateTime generatedAt)
        : base(id, tenantId)
    {
        PayslipId = payslipId;
        RunId = runId;
        Version = version;
        StepsJson = stepsJson;
        ParameterSnapshotId = parameterSnapshotId;
        GeneratedAt = generatedAt;
    }

    public Guid PayslipId { get; private set; }

    /// <summary>Groups every payslip's trace produced by the same "Tümünü/Değişenleri yeniden hesapla" run.</summary>
    public Guid RunId { get; private set; }

    public int Version { get; private set; }

    /// <summary>JSON array of trace steps ({Order, Code, TitleKey, Inputs, Formula, Output, ParameterRefs[]}).</summary>
    public string StepsJson { get; private set; } = string.Empty;

    /// <summary>Correlates to the set of LegislationParameter values resolved for this run (ADR 0008); no separate snapshot table yet, so this is the RunId until P3/P8 add one.</summary>
    public Guid ParameterSnapshotId { get; private set; }

    public DateTime GeneratedAt { get; private set; }

    public static PayslipCalculationTrace Create(
        Guid tenantId, Guid payslipId, Guid runId, int version, string stepsJson, Guid parameterSnapshotId, DateTime generatedAt)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(payslipId);
        Guard.NotDefault(runId);
        return new PayslipCalculationTrace(Guid.NewGuid(), tenantId, payslipId, runId, version, Guard.NotEmpty(stepsJson), parameterSnapshotId, generatedAt);
    }
}
