using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>
/// Stable reason codes a <see cref="PayslipDirtyFlag"/> can carry (11.3.07-bordro.md "Değişti mekanizması"). Map to
/// i18n keys <c>payroll.dirty.reason.*</c> (SharedResource{,.en}.resx) for the future "Değişenler" screen (P8).
/// </summary>
public static class PayslipDirtyFlagReasons
{
    /// <summary>Leave.Contracts.Events.OvertimeConvertedToPay: approved overtime paid in cash changes the month's earnings.</summary>
    public const string OvertimeChanged = "overtime_changed";

    /// <summary>A one-off earning (prim, komisyon) was added to or removed from the month.</summary>
    public const string AdjustmentChanged = "adjustment_changed";

    /// <summary>Employee.Contracts.Events.SalaryChanged.</summary>
    public const string SalaryChanged = "salary_changed";

    /// <summary>Payroll's own EmployeePayrollProfile created/updated (P2 EmployeePayrollProfileFeatures).</summary>
    public const string ProfileChanged = "profile_changed";

    /// <summary>Employee.Contracts.Events.EmployeeProfileChanged, Section=Bank.</summary>
    public const string BankChanged = "bank_changed";

    /// <summary>Leave.Contracts.Events.LeaveCancelled (the only Leave.Leave* event that exists today; LeaveApproved
    /// does not exist yet - see faz3-durum-takibi.md P3 entry).</summary>
    public const string LeaveChanged = "leave_changed";

    /// <summary><see cref="Periods.ChangeDetectionRun"/> hash mismatch ("event kaçırma sigortası").</summary>
    public const string HashMismatch = "hash_mismatch";

    /// <summary>A formula pay item (ödeme türü) was assigned, changed or removed, or its definition changed (PAY-FORMULA).</summary>
    public const string PayItemChanged = "pay_item_changed";
}

/// <summary>
/// One employee's "Değişti" flag for one open <see cref="Periods.PayrollPeriod"/> (11.3.07-bordro.md "SenseİK tasarım
/// kararı"). At most one ACTIVE (unresolved, <see cref="ClearedAt"/> null) flag exists per (period, employee) - callers
/// merge a new reason into the active flag instead of creating a second row (enforced by the Application layer via
/// the repository, mirroring how EmployeePayrollProfile's overlap rule works: no parent aggregate holds every sibling
/// in memory, so the invariant lives one level up). A cleared flag is kept (history), a fresh one is created if the
/// employee goes dirty again later in the same period.
/// </summary>
public sealed class PayslipDirtyFlag : TenantAggregateRoot<Guid>
{
    private readonly List<string> _reasons = [];

    private PayslipDirtyFlag()
    {
    }

    private PayslipDirtyFlag(Guid id, Guid tenantId, Guid payrollPeriodId, Guid employeeId, string reason, Guid? sourceEventId, DateTime detectedAt)
        : base(id, tenantId)
    {
        PayrollPeriodId = payrollPeriodId;
        EmployeeId = employeeId;
        _reasons.Add(reason);
        SourceEventId = sourceEventId;
        DetectedAt = detectedAt;
    }

    public Guid PayrollPeriodId { get; private set; }

    public Guid EmployeeId { get; private set; }

    /// <summary>Reason codes accumulated while this flag stayed active (<see cref="PayslipDirtyFlagReasons"/>); never empty.</summary>
    public IReadOnlyList<string> Reasons => _reasons;

    /// <summary>The integration event that most recently touched this flag, if any (ChangeDetectionRun mismatches have none).</summary>
    public Guid? SourceEventId { get; private set; }

    public DateTime DetectedAt { get; private set; }

    public DateTime? ClearedAt { get; private set; }

    /// <summary>The recalculation run (<see cref="Payslips.PayslipCalculationTrace.RunId"/>) that cleared this flag.</summary>
    public Guid? ClearedByRunId { get; private set; }

    public bool IsActive => ClearedAt is null;

    public static PayslipDirtyFlag Create(Guid tenantId, Guid payrollPeriodId, Guid employeeId, string reason, Guid? sourceEventId, DateTime detectedAt)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(payrollPeriodId);
        Guard.NotDefault(employeeId);
        return new PayslipDirtyFlag(Guid.NewGuid(), tenantId, payrollPeriodId, employeeId, Guard.NotEmpty(reason), sourceEventId, detectedAt);
    }

    /// <summary>Merges another reason into this still-active flag (idempotent: no duplicate reason codes).</summary>
    public void AddReason(string reason, Guid? sourceEventId, DateTime detectedAt)
    {
        if (!IsActive)
        {
            return;
        }

        if (!_reasons.Contains(reason, StringComparer.Ordinal))
        {
            _reasons.Add(reason);
        }

        SourceEventId = sourceEventId ?? SourceEventId;
        DetectedAt = detectedAt;
    }

    /// <summary>A successful recalculation run resolved this flag.</summary>
    public void Clear(Guid runId, DateTime clearedAt)
    {
        ClearedAt = clearedAt;
        ClearedByRunId = runId;
    }
}
