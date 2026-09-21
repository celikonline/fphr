using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Periods;

/// <summary>
/// One tenant-month of payroll (11.3.7 "Dönemler"). This slice only creates/reads Open periods; Approved/Paid/Locked
/// transitions, the "Değişenler" dirty-flag workflow and Submit/Lock actions are P3+.
/// </summary>
public sealed class PayrollPeriod : TenantAggregateRoot<Guid>, IAuditLogged
{
    public static IReadOnlyList<string> AuditedFields { get; } = [nameof(Status), nameof(ReopenReason)];

    private PayrollPeriod()
    {
    }

    private PayrollPeriod(Guid id, Guid tenantId, int year, int month)
        : base(id, tenantId)
    {
        Year = year;
        Month = month;
        Status = PayrollPeriodStatus.Open;
    }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public PayrollPeriodStatus Status { get; private set; }

    /// <summary>Why the period was last reopened after approval ("Yeniden aç" needs a reason and its own permission); null if it never was.</summary>
    public string? ReopenReason { get; private set; }

    public DateTimeOffset? ReopenedAt { get; private set; }

    public static Result<PayrollPeriod> Create(Guid tenantId, int year, int month)
    {
        Guard.NotDefault(tenantId);
        if (month is < 1 or > 12)
        {
            return Error.Validation(PayrollDomainErrors.PeriodMonthInvalid);
        }

        return new PayrollPeriod(Guid.NewGuid(), tenantId, year, month);
    }

    /// <summary>
    /// Minimal forward-only status transitions (Open → Approved → Paid → Locked, akış §4/§5 "Yayın ve tebliğ"/"Kilit
    /// ve çıktı") - just enough to make "Approved+ dönemde yeniden hesap reddedilir" (P3 §4) testable and to give P4+
    /// a real Status to build the Submit/MarkPaid/Lock workflow (approval gate, Workflow integration, permissions) on
    /// top of, rather than adding a full lifecycle here. No business rule is enforced beyond monotonic order.
    /// </summary>
    public Result Approve()
    {
        if (Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotOpen);
        }

        Status = PayrollPeriodStatus.Approved;
        return Result.Success();
    }

    public Result MarkPaid()
    {
        if (Status != PayrollPeriodStatus.Approved)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotApproved);
        }

        Status = PayrollPeriodStatus.Paid;
        return Result.Success();
    }

    public Result Lock()
    {
        if (Status != PayrollPeriodStatus.Paid)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotPaid);
        }

        Status = PayrollPeriodStatus.Locked;
        return Result.Success();
    }

    /// <summary>
    /// Sends an Approved, Paid or Locked period back to Open so it can be recalculated - a deliberate, audited exception to the
    /// forward-only flow: it needs a written reason (kept with the time) and a stronger permission than Approve.
    /// </summary>
    public Result Reopen(string reason, DateTimeOffset now)
    {
        if (Status == PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotReopenable);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > PayrollLimits.ReopenReasonMaxLength)
        {
            return Error.Validation(PayrollDomainErrors.PeriodReopenReasonInvalid);
        }

        Status = PayrollPeriodStatus.Open;
        ReopenReason = reason.Trim();
        ReopenedAt = now;
        return Result.Success();
    }
}
