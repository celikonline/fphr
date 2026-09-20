using Sense.Modules.Leave.Domain.RuleSets;

namespace Sense.Modules.Leave.Domain.Balances;

/// <summary>
/// 4857 md.56/3 (senseik-036): yıllık ücretli izin bölünebilir, ancak bölümlerden biri on günden az olamaz. The
/// Annual type's <see cref="LeaveTypeSettings.MinConsecutiveDays"/> therefore means "smallest permitted block" rather
/// than a per-request minimum (a per-request minimum would forbid every short annual leave, which the law does not do).
/// A short request is refused only when accepting it would leave the employee's remaining entitlement too small to
/// ever form that block and no block has been booked yet. When the balance already cannot hold a block (opening
/// balances, prorated/monthly accrual), the rule stays out of the way - it never strands leave nobody could use.
/// Not modelled: the "en çok üç bölüm" cap and the employer-side (HR on-behalf) decision, which bypasses this check.
/// </summary>
public static class AnnualLeaveSplitRule
{
    /// <summary>The rule governs the Annual type when it has a positive <see cref="LeaveTypeSettings.MinConsecutiveDays"/>.</summary>
    public static bool Applies(LeaveType type) => type.Code == LeaveTypeCode.Annual && type.MinConsecutiveDays is > 0m;

    /// <summary>
    /// True when a request of <paramref name="requestDays"/> (shorter than <paramref name="minimumBlockDays"/>) would take the
    /// balance from "a block still fits" (<paramref name="availableDays"/> ≥ block) to "no block fits".
    /// </summary>
    public static bool WouldForfeitMinimumBlock(decimal minimumBlockDays, decimal requestDays, decimal availableDays) =>
        requestDays < minimumBlockDays && availableDays >= minimumBlockDays && availableDays - requestDays < minimumBlockDays;

    /// <summary>True when one of the already booked (pending/approved) requests of the period is itself a full block.</summary>
    public static bool HasBookedBlock(decimal minimumBlockDays, IEnumerable<decimal> bookedRequestDays) =>
        bookedRequestDays.Any(days => days >= minimumBlockDays);
}
