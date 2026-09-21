using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Closing;

/// <summary>The four stages of the payroll closing checklist (Bordro Kapanış Kontrol Listesi).</summary>
public enum ClosingStage
{
    /// <summary>Bordro hazırlık: personel hareketleri, puantaj, ek ödeme ve kesintiler.</summary>
    Preparation = 0,

    /// <summary>Bordro hesaplama ve kontrol: parametreler, çalıştırma, onay ve mutabakat.</summary>
    Calculation = 1,

    /// <summary>Yasal bildirimler ve ödemeler: SGK, vergi, banka.</summary>
    Legal = 2,

    /// <summary>Arşiv ve raporlama.</summary>
    Archive = 3,
}

/// <summary>Whether the system can decide a check on its own or a person ticks it off.</summary>
public enum ClosingCheckKind
{
    Automatic = 0,
    Manual = 1,
}

/// <summary>A failed <see cref="Blocker"/> stops the period from moving on (approve / lock); a <see cref="Warning"/> only shows.</summary>
public enum ClosingCheckSeverity
{
    Blocker = 0,
    Warning = 1,
}

public enum ClosingCheckStatus
{
    Pending = 0,
    Passed = 1,
    Failed = 2,
    Waived = 3,
}

/// <summary>One item of the closing checklist template. <see cref="Code"/> is the stable key the UI translates.</summary>
public sealed record ClosingCheckDefinition(string Code, ClosingStage Stage, ClosingCheckKind Kind, ClosingCheckSeverity Severity);

/// <summary>
/// The checklist template every period gets ("Kolay İK 2026 Bordro Kapanış Kontrol Listesi" mapped to four stages). Automatic
/// items are decided by rules over Payroll's own data; the rest are manual tick-offs. Deadlines and thresholds are never
/// baked in here - they come from legislation parameters where a rule needs them.
/// </summary>
public static class ClosingCheckCatalog
{
    private static ClosingCheckDefinition Manual(string code, ClosingStage stage) => new(code, stage, ClosingCheckKind.Manual, ClosingCheckSeverity.Warning);

    private static ClosingCheckDefinition AutoWarning(string code, ClosingStage stage) => new(code, stage, ClosingCheckKind.Automatic, ClosingCheckSeverity.Warning);

    private static ClosingCheckDefinition Auto(string code, ClosingStage stage) => new(code, stage, ClosingCheckKind.Automatic, ClosingCheckSeverity.Blocker);

    public static IReadOnlyList<ClosingCheckDefinition> All { get; } =
    [
        // 1. Bordro hazırlık
        Manual("HIRE_NOTICES", ClosingStage.Preparation),
        Manual("EXIT_NOTICES", ClosingStage.Preparation),
        Manual("CAREER_CHANGES", ClosingStage.Preparation),
        Manual("PROBATION_ENDINGS", ClosingStage.Preparation),
        Manual("CONTRACT_RENEWALS", ClosingStage.Preparation),
        Manual("PDKS_VERIFIED", ClosingStage.Preparation),
        Manual("MISSING_PUNCHES", ClosingStage.Preparation),
        Manual("LEAVE_USAGE", ClosingStage.Preparation),
        Manual("PENDING_LEAVE_REQUESTS", ClosingStage.Preparation),
        Manual("SICKNESS_REPORTS", ClosingStage.Preparation),
        Manual("OVERTIME_APPROVED", ClosingStage.Preparation),
        Manual("BONUSES_COLLECTED", ClosingStage.Preparation),
        Manual("ADVANCES_CHECKED", ClosingStage.Preparation),
        Manual("GARNISHMENTS_UPDATED", ClosingStage.Preparation),
        Manual("BES_VERIFIED", ClosingStage.Preparation),
        Manual("BENEFITS_PROCESSED", ClosingStage.Preparation),

        // 2. Bordro hesaplama ve kontrol
        Auto("LEGISLATION_PARAMETERS", ClosingStage.Calculation),
        Auto("PAYSLIPS_GENERATED", ClosingStage.Calculation),
        Auto("NO_DIRTY_PAYSLIPS", ClosingStage.Calculation),
        Auto("NET_NOT_NEGATIVE", ClosingStage.Calculation),
        AutoWarning("EMPLOYEES_WITHOUT_PAYSLIP", ClosingStage.Calculation),
        Manual("PARAMETERS_VERIFIED", ClosingStage.Calculation),
        Manual("TRIAL_RUN", ClosingStage.Calculation),
        Manual("PREVIOUS_MONTH_COMPARISON", ClosingStage.Calculation),
        Manual("VARIANCE_REVIEWED", ClosingStage.Calculation),
        Manual("SAMPLE_NET_CHECK", ClosingStage.Calculation),
        Manual("PROFILE_CALC_CHECK", ClosingStage.Calculation),
        Manual("FINANCE_APPROVAL", ClosingStage.Calculation),
        Manual("RECONCILIATION_DONE", ClosingStage.Calculation),

        // 3. Yasal bildirimler ve ödemeler
        Manual("APHB_PREPARED", ClosingStage.Legal),
        Manual("APHB_SENT", ClosingStage.Legal),
        Manual("MUHSGK_PREPARED", ClosingStage.Legal),
        Manual("MUHSGK_SENT", ClosingStage.Legal),
        Manual("SGK_PAYMENT", ClosingStage.Legal),
        Manual("TAX_PAYMENT", ClosingStage.Legal),
        Manual("BANK_FILE_CREATED", ClosingStage.Legal),
        Manual("BANK_TOTALS_MATCH", ClosingStage.Legal),
        Manual("SALARIES_PAID", ClosingStage.Legal),
        Manual("PAYSLIPS_DELIVERED", ClosingStage.Legal),
        Manual("BES_INSTRUCTION_SENT", ClosingStage.Legal),
        Manual("GARNISHMENT_TRANSFERRED", ClosingStage.Legal),

        // 4. Arşiv ve raporlama
        Manual("PAYSLIPS_ARCHIVED", ClosingStage.Archive),
        Manual("APHB_RECEIPTS_SAVED", ClosingStage.Archive),
        Manual("ACCRUAL_RECEIPTS_SAVED", ClosingStage.Archive),
        Manual("BANK_RECEIPTS_ARCHIVED", ClosingStage.Archive),
        Manual("CHANGE_LOG_CREATED", ClosingStage.Archive),
        Manual("MANAGEMENT_REPORTS", ClosingStage.Archive),
    ];

    public static ClosingCheckDefinition? Find(string code) => All.FirstOrDefault(d => d.Code == code);
}

/// <summary>
/// One checklist item of one period: created from <see cref="ClosingCheckCatalog"/> when the checklist is first opened, then
/// decided by a rule (automatic) or ticked off by a person (manual), or waived with a written reason.
/// </summary>
public sealed class PeriodClosingCheck : TenantAggregateRoot<Guid>
{
    private PeriodClosingCheck()
    {
    }

    private PeriodClosingCheck(Guid id, Guid tenantId, Guid periodId, ClosingCheckDefinition definition)
        : base(id, tenantId)
    {
        PeriodId = periodId;
        Code = definition.Code;
        Stage = definition.Stage;
        Kind = definition.Kind;
        Severity = definition.Severity;
        Status = ClosingCheckStatus.Pending;
    }

    public Guid PeriodId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public ClosingStage Stage { get; private set; }

    public ClosingCheckKind Kind { get; private set; }

    public ClosingCheckSeverity Severity { get; private set; }

    public ClosingCheckStatus Status { get; private set; }

    /// <summary>What the rule found (failed items list what is wrong) or the person's note.</summary>
    public string? Detail { get; private set; }

    /// <summary>Why the check was waived; required for <see cref="ClosingCheckStatus.Waived"/>.</summary>
    public string? WaiverReason { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Whether this item lets the period move on: passed or waived.</summary>
    public bool IsSatisfied => Status is ClosingCheckStatus.Passed or ClosingCheckStatus.Waived;

    public static PeriodClosingCheck Create(Guid tenantId, Guid periodId, ClosingCheckDefinition definition)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(periodId);
        return new PeriodClosingCheck(Guid.NewGuid(), tenantId, periodId, definition);
    }

    /// <summary>Applies a rule's verdict to an automatic check; a waived check stays waived.</summary>
    public void ApplyRuleResult(bool passed, string? detail, DateTimeOffset now)
    {
        if (Status == ClosingCheckStatus.Waived)
        {
            return;
        }

        Status = passed ? ClosingCheckStatus.Passed : ClosingCheckStatus.Failed;
        Detail = detail;
        DecidedByUserId = null;
        DecidedAt = now;
    }

    /// <summary>A person ticks a manual check off (optionally with a note).</summary>
    public Result Complete(string? note, Guid? userId, DateTimeOffset now)
    {
        if (Kind != ClosingCheckKind.Manual)
        {
            return Error.Validation(PayrollDomainErrors.ClosingCheckNotManual);
        }

        Status = ClosingCheckStatus.Passed;
        Detail = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        WaiverReason = null;
        DecidedByUserId = userId;
        DecidedAt = now;
        return Result.Success();
    }

    /// <summary>Lets the period move on despite a failed or pending check; a written reason is mandatory.</summary>
    public Result Waive(string reason, Guid? userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > PayrollLimits.ClosingCheckTextMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.ClosingWaiverReasonInvalid);
        }

        Status = ClosingCheckStatus.Waived;
        WaiverReason = reason.Trim();
        DecidedByUserId = userId;
        DecidedAt = now;
        return Result.Success();
    }

    /// <summary>Puts a decided manual check back to pending (e.g. the period was reopened).</summary>
    public void Reset()
    {
        Status = ClosingCheckStatus.Pending;
        Detail = null;
        WaiverReason = null;
        DecidedByUserId = null;
        DecidedAt = null;
    }
}
