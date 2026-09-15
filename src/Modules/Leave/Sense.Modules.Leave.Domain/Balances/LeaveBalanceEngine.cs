using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.RuleSets;

namespace Sense.Modules.Leave.Domain.Balances;

/// <summary>Statutory annual leave floor resolved from LegislationParameter for one date (md.53).</summary>
public sealed record StatutoryAnnualLeave(
    decimal Tier1Days, int Tier2AfterYears, decimal Tier2Days, int Tier3AfterYears, decimal Tier3Days,
    int YoungAgeMax, int OldAgeMin, decimal AgeExceptionMinDays)
{
    public static StatutoryAnnualLeave Turkey2026 { get; } = new(14m, 5, 20m, 15, 26m, 18, 50, 20m);

    /// <summary>Legal minimum for a completed service year count: 1-5 → 14, &gt;5 and &lt;15 → 20, ≥15 → 26; age ≤18/≥50 → at least 20. Below 1 year → 0.</summary>
    public decimal MinimumDays(int completedYears, int? age)
    {
        if (completedYears < 1)
        {
            return 0m;
        }

        var days = completedYears >= Tier3AfterYears ? Tier3Days : completedYears > Tier2AfterYears ? Tier2Days : Tier1Days;
        return age is { } a && (a <= YoungAgeMax || a >= OldAgeMin) ? Math.Max(days, AgeExceptionMinDays) : days;
    }
}

/// <summary>One accrual period [From, To] (both inclusive).</summary>
public readonly record struct LeavePeriod(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly date) => From <= date && date <= To;

    public LeavePeriod Next(AccrualBasis basis, DateOnly accrualStart) => LeaveBalanceEngine.PeriodContaining(basis, accrualStart, To.AddDays(1));
}

/// <summary>
/// Pure balance engine for PerYear types: period boundaries, yearly entitlement (tiers, age exception, statutory
/// floor for Code=Annual), regeneration of Accrual/CarryOver rows from the preserved rows, and the balance projection.
/// </summary>
public static class LeaveBalanceEngine
{
    private const int MonthsPerYear = 12;
    private const decimal DaysPerYear = 365m;

    public static int CompletedYears(DateOnly start, DateOnly asOf)
    {
        if (asOf < start)
        {
            return 0;
        }

        var years = asOf.Year - start.Year;
        return start.AddYears(years) > asOf ? years - 1 : years;
    }

    public static LeavePeriod PeriodContaining(AccrualBasis basis, DateOnly accrualStart, DateOnly date)
    {
        if (basis == AccrualBasis.CalendarYear)
        {
            return new LeavePeriod(new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31));
        }

        var years = CompletedYears(accrualStart, date);
        var from = accrualStart.AddYears(years);
        return new LeavePeriod(from, accrualStart.AddYears(years + 1).AddDays(-1));
    }

    /// <summary>Entitlement for a full period whose accrual falls on <paramref name="accrualDate"/>.</summary>
    public static decimal YearlyEntitlement(LeaveType type, DateOnly accrualStart, DateOnly accrualDate, DateOnly? birthDate, StatutoryAnnualLeave? statutory)
    {
        var years = CompletedYears(accrualStart, accrualDate);
        var days = (type.AnnualDays ?? 0m) + type.SeniorityTiers.Where(t => t.AppliesTo(years)).Sum(t => t.ExtraDays);
        int? age = birthDate is { } b ? CompletedYears(b, accrualDate) : null;

        if (type.AgeExceptionEnabled && type.AgeExceptionMinDays is { } minDays && age is { } a && statutory is not null
            && (a <= statutory.YoungAgeMax || a >= statutory.OldAgeMin))
        {
            days = Math.Max(days, minDays);
        }

        if (type.Code == LeaveTypeCode.Annual && statutory is not null)
        {
            days = Math.Max(days, statutory.MinimumDays(years, age));
        }

        return days;
    }

    /// <summary>
    /// Regenerates Accrual and CarryOver rows from <paramref name="accrualStart"/> up to <paramref name="asOf"/>, keeping
    /// <paramref name="preserved"/> (Usage/ExtraGrant/Adjustment/OpeningBalance) as the input of each period's closing balance.
    /// </summary>
    public static IReadOnlyList<LeaveLedgerEntry> Regenerate(
        LeaveType type, Guid tenantId, Guid employeeId, DateOnly accrualStart, DateOnly? birthDate, DateOnly asOf,
        IReadOnlyCollection<LeaveLedgerEntry> preserved, StatutoryAnnualLeave? statutory)
    {
        var generated = new List<LeaveLedgerEntry>();
        if (!type.HasBalance || asOf < accrualStart)
        {
            return generated;
        }

        var period = PeriodContaining(type.AccrualBasis, accrualStart, accrualStart);
        var previous = (LeavePeriod?)null;
        while (period.From <= asOf)
        {
            if (previous is { } prev)
            {
                var closing = preserved.Where(e => prev.Contains(e.EffectiveDate)).Sum(e => e.Amount)
                    + generated.Where(e => prev.Contains(e.EffectiveDate)).Sum(e => e.Amount);
                var carried = CarryOverAmount(type, closing);
                if (carried != 0m)
                {
                    generated.Add(LeaveLedgerEntry.CarryOver(tenantId, employeeId, type.Id, carried, period.From));
                }
            }

            foreach (var (date, amount) in AccrualsFor(type, accrualStart, birthDate, period, statutory))
            {
                if (date <= asOf && amount != 0m)
                {
                    generated.Add(LeaveLedgerEntry.Accrual(tenantId, employeeId, type.Id, amount, date));
                }
            }

            previous = period;
            period = period.Next(type.AccrualBasis, accrualStart);
        }

        return generated;
    }

    public static decimal CarryOverAmount(LeaveType type, decimal closingBalance) => type.CarryOverMode switch
    {
        CarryOverMode.Unlimited => closingBalance,
        CarryOverMode.Limited => closingBalance < 0m ? closingBalance : Math.Min(closingBalance, type.CarryOverMaxDays ?? 0m),
        _ => 0m,
    };

    /// <summary>
    /// Accrual rows attributable to <paramref name="period"/>. StartOfPeriod rows are dated at the period start; EndOfPeriod
    /// rows are dated the day after the period end (so they are usable in - and belong to - the following period).
    /// A partial first calendar-year period is prorated by days.
    /// </summary>
    public static IEnumerable<(DateOnly Date, decimal Amount)> AccrualsFor(
        LeaveType type, DateOnly accrualStart, DateOnly? birthDate, LeavePeriod period, StatutoryAnnualLeave? statutory)
    {
        var effectiveFrom = period.From < accrualStart ? accrualStart : period.From;
        var periodDays = period.To.DayNumber - period.From.DayNumber + 1;
        var coveredDays = period.To.DayNumber - effectiveFrom.DayNumber + 1;
        var ratio = coveredDays >= periodDays ? 1m : (decimal)coveredDays / periodDays;
        var endDate = period.To.AddDays(1);

        switch (type.AccrualFrequency)
        {
            case AccrualFrequency.Yearly:
            {
                var date = type.AccrualTiming == AccrualTiming.StartOfPeriod ? effectiveFrom : endDate;
                yield return (date, decimal.Round(YearlyEntitlement(type, accrualStart, date, birthDate, statutory) * ratio, 4));
                break;
            }

            default:
            {
                for (var monthStart = effectiveFrom; monthStart <= period.To; monthStart = monthStart.AddMonths(1))
                {
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    if (monthEnd > period.To)
                    {
                        monthEnd = period.To;
                    }

                    var date = type.AccrualTiming == AccrualTiming.StartOfPeriod ? monthStart : monthEnd.AddDays(1);
                    var yearly = YearlyEntitlement(type, accrualStart, monthStart, birthDate, statutory);
                    var amount = type.AccrualFrequency == AccrualFrequency.Monthly
                        ? yearly / MonthsPerYear
                        : yearly / DaysPerYear * (monthEnd.DayNumber - monthStart.DayNumber + 1);
                    yield return (date, decimal.Round(amount, 4));
                }

                break;
            }
        }
    }

    /// <summary>Official "İzin Özeti" projection: usage counts by leave START date (≤ today → used, later → future approved).</summary>
    public static LeaveBalance Project(LeaveType type, DateOnly accrualStart, DateOnly today, IReadOnlyCollection<LeaveLedgerEntry> entries)
    {
        var period = PeriodContaining(type.AccrualBasis, accrualStart, today < accrualStart ? accrualStart : today);
        static decimal Used(IEnumerable<LeaveLedgerEntry> rows) => -rows.Where(e => e.Type == LeaveLedgerEntryType.Usage).Sum(e => e.Amount);

        var inPeriod = entries.Where(e => period.Contains(e.EffectiveDate)).ToList();
        var pastInPeriod = inPeriod.Where(e => e.EffectiveDate <= today).ToList();
        var carriedIn = inPeriod.Where(e => e.Type == LeaveLedgerEntryType.CarryOver).Sum(e => e.Amount);
        var accruedInPeriod = inPeriod.Where(e => e.Type == LeaveLedgerEntryType.Accrual && e.EffectiveDate <= today).Sum(e => e.Amount);
        var currentPeriodUsed = Used(pastInPeriod);
        var futureApproved = Used(inPeriod.Where(e => e.EffectiveDate > today));
        var currentBalance = pastInPeriod.Where(e => e.Type != LeaveLedgerEntryType.Usage).Sum(e => e.Amount) - currentPeriodUsed;

        var negativeLimit = type.NegativeBalance switch
        {
            NegativeBalanceMode.Limited => type.NegativeBalanceLimitDays,
            NegativeBalanceMode.None => 0m,
            _ => null,
        };

        return new LeaveBalance(
            type.Id,
            period.From,
            period.To,
            period.To.AddDays(1),
            TotalAccrued: entries.Where(e => e.Type == LeaveLedgerEntryType.Accrual && e.EffectiveDate <= today).Sum(e => e.Amount),
            TotalUsed: Used(entries.Where(e => e.EffectiveDate <= today)),
            ExtraGrants: pastInPeriod.Where(e => e.Type == LeaveLedgerEntryType.ExtraGrant).Sum(e => e.Amount),
            Adjustments: pastInPeriod.Where(e => e.Type is LeaveLedgerEntryType.Adjustment or LeaveLedgerEntryType.OpeningBalance).Sum(e => e.Amount),
            CarriedOverIntoPeriod: carriedIn,
            LastPeriodAccrued: accruedInPeriod,
            CurrentPeriodUsed: currentPeriodUsed,
            FutureApprovedInPeriod: futureApproved,
            NextPeriodApproved: Used(entries.Where(e => e.EffectiveDate > period.To)),
            CurrentBalance: currentBalance,
            AvailableBalance: currentBalance - futureApproved,
            NegativeBalanceLimit: negativeLimit);
    }
}

/// <summary>Balance summary of one PerYear type (field names follow Kolay İK "İzin Özeti", 11.3.5).</summary>
public sealed record LeaveBalance(
    Guid LeaveTypeId,
    DateOnly CurrentPeriodFrom,
    DateOnly CurrentPeriodTo,
    DateOnly NextAccrualDate,
    decimal TotalAccrued,
    decimal TotalUsed,
    decimal ExtraGrants,
    decimal Adjustments,
    decimal CarriedOverIntoPeriod,
    decimal LastPeriodAccrued,
    decimal CurrentPeriodUsed,
    decimal FutureApprovedInPeriod,
    decimal NextPeriodApproved,
    decimal CurrentBalance,
    decimal AvailableBalance,
    decimal? NegativeBalanceLimit);
