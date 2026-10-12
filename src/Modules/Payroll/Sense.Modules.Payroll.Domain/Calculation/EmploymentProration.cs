namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Kıst (partial-month) working days for a payroll period, 30-day basis (SGK prim günü / ücret esası): every month is
/// treated as 30 days, so a full month is always 30 regardless of its calendar length. Pure - the Application layer
/// feeds the employee's hire/termination dates.
/// </summary>
public static class EmploymentProration
{
    /// <summary>Days in a payroll month on the 30-day basis.</summary>
    public const int PayrollMonthDays = 30;

    /// <summary>
    /// Paid days (30-day basis) for the intersection of [<paramref name="hireDate"/>, <paramref name="terminationDate"/>]
    /// with [<paramref name="periodStart"/>, <paramref name="periodEnd"/>]. 0 = the employee was not employed at all in
    /// the period; <see cref="PayrollMonthDays"/> = full month (also when the employee left/started on the last/first
    /// day). A null date means "unbounded" on that side. A start on the 31st counts as day 30, so the minimum is 1.
    /// </summary>
    public static int PaidDays(DateOnly periodStart, DateOnly periodEnd, DateOnly? hireDate, DateOnly? terminationDate)
    {
        var start = hireDate is { } hired && hired > periodStart ? hired : periodStart;
        var end = terminationDate is { } terminated && terminated < periodEnd ? terminated : periodEnd;
        if (start > end)
        {
            return 0;
        }

        var startDay = Math.Min(start.Day, PayrollMonthDays);
        var endDay = end == periodEnd ? PayrollMonthDays : Math.Min(end.Day, PayrollMonthDays);
        return Math.Max(1, endDay - startDay + 1);
    }

    /// <summary>The last day the employee is employed inside the period (termination date when it falls before the period end).</summary>
    public static DateOnly EffectiveEnd(DateOnly periodEnd, DateOnly? terminationDate) =>
        terminationDate is { } terminated && terminated < periodEnd ? terminated : periodEnd;

    /// <summary>The first day the employee is employed inside the period (hire date when it falls after the period start).</summary>
    public static DateOnly EffectiveStart(DateOnly periodStart, DateOnly? hireDate) =>
        hireDate is { } hired && hired > periodStart ? hired : periodStart;
}
