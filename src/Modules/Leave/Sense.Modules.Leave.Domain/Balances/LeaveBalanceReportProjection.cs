using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.RuleSets;

namespace Sense.Modules.Leave.Domain.Balances;

/// <summary>Report-specific lifetime totals; the employee card's Reset-mode projection stays unchanged.</summary>
public sealed record LeaveBalanceReportProjection(
    LeaveBalance Balance, decimal LifetimeExtraGrants, decimal LifetimeEntitled, decimal LifetimeUsed,
    decimal CarryOverLoss, decimal AvailableCarriedOver, decimal PeriodOpeningBalance, decimal NextPeriodApproved)
{
    public static LeaveBalanceReportProjection Project(LeaveType type, DateOnly accrualStart, DateOnly today, IReadOnlyCollection<LeaveLedgerEntry> entries)
    {
        var balance = LeaveBalanceEngine.Project(type, accrualStart, today, entries);
        var past = entries.Where(e => e.EffectiveDate <= today).ToList();
        var extra = past.Where(e => e.Type == LeaveLedgerEntryType.ExtraGrant).Sum(e => e.Amount);
        var period = new LeavePeriod(balance.CurrentPeriodFrom, balance.CurrentPeriodTo);
        var next = period.Next(type.AccrualBasis, accrualStart);
        var nextUsed = -entries.Where(e => next.Contains(e.EffectiveDate) && IsUsage(e)).Sum(e => e.Amount);
        return new(balance, extra, past.Where(e => e.Type == LeaveLedgerEntryType.Accrual).Sum(e => e.Amount) + extra,
            -past.Where(IsUsage).Sum(e => e.Amount), LeaveBalanceEngine.CarryOverLoss(period, entries),
            Math.Max(0m, balance.CarriedOverIntoPeriod - Math.Max(0m, balance.CurrentPeriodUsed)),
            BalanceAt(type, accrualStart, period.From, entries, opening: true), nextUsed);
    }

    /// <summary>Period ledger replay; opening includes same-day grants/carry/adjustments, before same-day usage.</summary>
    public static decimal BalanceAt(LeaveType type, DateOnly accrualStart, DateOnly date, IReadOnlyCollection<LeaveLedgerEntry> entries, bool opening = false)
    {
        if (date < accrualStart)
        {
            return 0m;
        }
        var period = LeaveBalanceEngine.PeriodContaining(type.AccrualBasis, accrualStart, date);
        return entries.Where(e => period.Contains(e.EffectiveDate) && e.EffectiveDate <= date
            && (!opening || e.EffectiveDate < date || !IsUsage(e))).Sum(e => e.Amount);
    }

    private static bool IsUsage(LeaveLedgerEntry entry) => entry.Type is LeaveLedgerEntryType.Usage or LeaveLedgerEntryType.Reversal;
}
