using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Ledger;

/// <summary>
/// Append-only leave ledger row (decimal(9,4) days). Amount is signed: Usage is negative, Accrual/ExtraGrant positive,
/// CarryOver/Adjustment/OpeningBalance either sign, Reversal positive (offsets a Usage row). A period's balance is the sum of the rows dated inside it, where the
/// CarryOver row at the period start brings the previous period's (possibly negative) closing balance forward.
/// <see cref="EffectiveDate"/> of a Usage row is the leave START date - balance fields are projected by it, not by
/// the time the row was recorded.
/// </summary>
public sealed class LeaveLedgerEntry : TenantEntity<Guid>
{
    private LeaveLedgerEntry()
    {
    }

    private LeaveLedgerEntry(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, LeaveLedgerEntryType type, decimal amount, DateOnly effectiveDate,
        LeaveLedgerSource source, Guid? sourceRefId, string? reason, string? idempotencyKey)
        : base(Guid.CreateVersion7(), tenantId)
    {
        EmployeeId = employeeId;
        LeaveTypeId = leaveTypeId;
        Type = type;
        Amount = decimal.Round(amount, 4);
        EffectiveDate = effectiveDate;
        Source = source;
        SourceRefId = sourceRefId;
        Reason = reason;
        IdempotencyKey = idempotencyKey;
    }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public LeaveLedgerEntryType Type { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public LeaveLedgerSource Source { get; private set; }

    public Guid? SourceRefId { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>(EmployeeId, LeaveTypeId, PeriodStart, Type) style key for system-generated rows; unique per tenant.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True for rows a recalculation regenerates (Accrual/CarryOver); Usage/ExtraGrant/Adjustment/OpeningBalance are preserved.</summary>
    public bool IsGenerated => Type is LeaveLedgerEntryType.Accrual or LeaveLedgerEntryType.CarryOver;

    public static LeaveLedgerEntry Accrual(Guid tenantId, Guid employeeId, Guid leaveTypeId, decimal days, DateOnly effectiveDate) =>
        new(tenantId, employeeId, leaveTypeId, LeaveLedgerEntryType.Accrual, days, effectiveDate, LeaveLedgerSource.System, null, null,
            GeneratedKey(employeeId, leaveTypeId, effectiveDate, LeaveLedgerEntryType.Accrual));

    public static LeaveLedgerEntry CarryOver(Guid tenantId, Guid employeeId, Guid leaveTypeId, decimal days, DateOnly periodStart) =>
        new(tenantId, employeeId, leaveTypeId, LeaveLedgerEntryType.CarryOver, days, periodStart, LeaveLedgerSource.System, null, null,
            GeneratedKey(employeeId, leaveTypeId, periodStart, LeaveLedgerEntryType.CarryOver));

    /// <summary>Approved leave usage dated at the leave start date; <paramref name="days"/> is positive, stored negative.</summary>
    public static LeaveLedgerEntry Usage(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, decimal days, DateOnly leaveStartDate, LeaveLedgerSource source, Guid? sourceRefId) =>
        new(tenantId, employeeId, leaveTypeId, LeaveLedgerEntryType.Usage, -Math.Abs(days), leaveStartDate, source, sourceRefId, null, null);

    /// <summary>Offsets a Usage row (cancelled approved leave): same type and effective date, positive amount.</summary>
    public static LeaveLedgerEntry Reversal(LeaveLedgerEntry usage, string? reason) =>
        new(usage.TenantId, usage.EmployeeId, usage.LeaveTypeId, LeaveLedgerEntryType.Reversal, Math.Abs(usage.Amount), usage.EffectiveDate, usage.Source,
            usage.SourceRefId, reason is { Length: > LeaveLimits.ReasonMaxLength } ? reason[..LeaveLimits.ReasonMaxLength] : reason, null);

    /// <summary>
    /// Rule set reassignment: a preserved row moves to the counterpart type (same Code) of the employee's new rule set so
    /// balances keep counting it. Generated rows are regenerated instead of moved.
    /// </summary>
    public void MoveToLeaveType(Guid leaveTypeId) => LeaveTypeId = leaveTypeId;

    public static Result<LeaveLedgerEntry> Adjustment(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, decimal days, DateOnly effectiveDate, string reason)
    {
        if (days == 0m)
        {
            return Error.Validation(LeaveDomainErrors.LedgerAmountZero);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > LeaveLimits.ReasonMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.LedgerReasonRequired);
        }

        return new LeaveLedgerEntry(tenantId, employeeId, leaveTypeId, LeaveLedgerEntryType.Adjustment, days, effectiveDate, LeaveLedgerSource.Manual, null, reason.Trim(), null);
    }

    public static Result<LeaveLedgerEntry> ExtraGrant(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, decimal days, DateOnly effectiveDate, LeaveLedgerSource source, Guid? sourceRefId, string? reason)
    {
        if (days <= 0m)
        {
            return Error.Validation(LeaveDomainErrors.LedgerAmountZero);
        }

        return new LeaveLedgerEntry(tenantId, employeeId, leaveTypeId, LeaveLedgerEntryType.ExtraGrant, days, effectiveDate, source, sourceRefId, reason, null);
    }

    private static string GeneratedKey(Guid employeeId, Guid leaveTypeId, DateOnly date, LeaveLedgerEntryType type) =>
        $"{employeeId:N}:{leaveTypeId:N}:{date:yyyyMMdd}:{type}";
}
