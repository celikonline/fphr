using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Payroll.Domain.Periods;

/// <summary>What triggered a <see cref="ChangeDetectionRun"/> (11.3.07-bordro.md "Değişti mekanizması").</summary>
public enum ChangeDetectionTrigger
{
    /// <summary>The periodic hash-reconciliation job ("5 dk mutabakat", P3 hardcoded default - see ChangeDetectionHostedService).</summary>
    Scheduled = 0,

    /// <summary>An integration event (SalaryChanged/EmployeeProfileChanged/...) drove an on-demand scan (not used by P3's event handlers, which write flags directly without a run row; reserved for a future manual "scan now" trigger).</summary>
    Event = 1,

    /// <summary>An operator-triggered scan (not exposed by any P3 endpoint; reserved for a future "Şimdi tara" action).</summary>
    Manual = 2,
}

/// <summary>
/// One hash-reconciliation pass over one open <see cref="PayrollPeriod"/> ("event kaçırma sigortası", 11.3.07-bordro.md
/// "SenseİK tasarım kararı"): compares every payslip's stored <c>InputHash</c> against a hash of current live inputs
/// (salary + payroll profile) and flags mismatches (<see cref="Payslips.PayslipDirtyFlagReasons.HashMismatch"/>).
/// </summary>
public sealed class ChangeDetectionRun : TenantEntity<Guid>
{
    private ChangeDetectionRun()
    {
    }

    private ChangeDetectionRun(Guid id, Guid tenantId, Guid periodId, ChangeDetectionTrigger trigger, DateTime startedAt)
        : base(id, tenantId)
    {
        PeriodId = periodId;
        Trigger = trigger;
        StartedAt = startedAt;
    }

    public Guid PeriodId { get; private set; }

    public ChangeDetectionTrigger Trigger { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    public int ScannedCount { get; private set; }

    public int DirtyCount { get; private set; }

    public static ChangeDetectionRun Start(Guid tenantId, Guid periodId, ChangeDetectionTrigger trigger, DateTime startedAt)
    {
        Guard.NotDefault(tenantId);
        Guard.NotDefault(periodId);
        return new ChangeDetectionRun(Guid.NewGuid(), tenantId, periodId, trigger, startedAt);
    }

    public void Finish(int scannedCount, int dirtyCount, DateTime finishedAt)
    {
        ScannedCount = scannedCount;
        DirtyCount = dirtyCount;
        FinishedAt = finishedAt;
    }
}
