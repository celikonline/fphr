namespace Sense.Modules.Leave.Domain.Overtime;

/// <summary>A planned working interval (local wall-clock) of an employee's schedule.</summary>
public readonly record struct ScheduledInterval(DateTime Start, DateTime End);

/// <summary>Result of the consent check for one overtime record.</summary>
public enum OvertimeConsentState
{
    InEffect = 0,
    Missing = 1,
    Revoked = 2,
}

/// <summary>Pure overtime calculations (payable minutes, night work, consent state, time-off conversion).</summary>
public static class OvertimeMath
{
    /// <summary>
    /// Daily work minutes used to turn converted minutes into leave days until the work schedule contract exists (roadmap #23):
    /// 4857 md.63 caps weekly work at 45 hours spread over 6 days = 7.5 hours/day, the same basis as Payroll.MonthlyHoursDivisor
    /// (225 = 30 × 7.5). A tenant-specific schedule (e.g. 9 h Mon–Fri) will replace this.
    /// </summary>
    public const int DefaultDailyWorkMinutes = 450;

    /// <summary>Default time-off rate: 4857 md.41 — 1 hour 30 minutes of free time per overtime hour.</summary>
    public const decimal DefaultTimeOffRate = 1.5m;

    /// <summary>Night period per 4857 md.69: 20:00–06:00.</summary>
    public static readonly TimeOnly NightStart = new(20, 0);

    public static readonly TimeOnly NightEnd = new(6, 0);

    /// <summary>Minutes of [start, start + duration) that fall inside any of the planned intervals (intervals may overlap each other).</summary>
    public static int OverlapMinutes(DateTime start, int durationMinutes, IEnumerable<ScheduledInterval> intervals)
    {
        var end = start.AddMinutes(durationMinutes);
        var clipped = intervals
            .Select(i => (Start: i.Start < start ? start : i.Start, End: i.End > end ? end : i.End))
            .Where(i => i.Start < i.End)
            .OrderBy(i => i.Start)
            .ToList();

        var total = TimeSpan.Zero;
        DateTime? curStart = null;
        var curEnd = DateTime.MinValue;
        foreach (var (s, e) in clipped)
        {
            if (curStart is null || s > curEnd)
            {
                if (curStart is { } cs)
                {
                    total += curEnd - cs;
                }

                curStart = s;
                curEnd = e;
            }
            else if (e > curEnd)
            {
                curEnd = e;
            }
        }

        if (curStart is { } last)
        {
            total += curEnd - last;
        }

        return (int)Math.Round(total.TotalMinutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>True if any minute of the record is inside a 20:00–06:00 night period.</summary>
    public static bool TouchesNight(DateTime start, int durationMinutes)
    {
        var nights = new List<ScheduledInterval>();
        for (var day = DateOnly.FromDateTime(start).AddDays(-1); day <= DateOnly.FromDateTime(start.AddMinutes(durationMinutes)); day = day.AddDays(1))
        {
            nights.Add(new ScheduledInterval(day.ToDateTime(NightStart), day.AddDays(1).ToDateTime(NightEnd)));
        }

        return OverlapMinutes(start, durationMinutes, nights) > 0;
    }

    /// <summary>Consent state on <paramref name="date"/>: any consent in effect wins; otherwise Revoked if one was revoked by then, else Missing.</summary>
    public static OvertimeConsentState ConsentState(IEnumerable<OvertimeConsent> consents, DateOnly date, out OvertimeConsent? inEffect)
    {
        var list = consents.ToList();
        inEffect = list.Where(c => c.IsInEffectOn(date)).OrderByDescending(c => c.CoversNightWork).FirstOrDefault();
        if (inEffect is not null)
        {
            return OvertimeConsentState.InEffect;
        }

        return list.Any(c => c.IsRevokedOn(date)) ? OvertimeConsentState.Revoked : OvertimeConsentState.Missing;
    }

    /// <summary>minutes × rate (rounded to whole minutes) and the equivalent leave days (4 decimals, the ledger precision).</summary>
    public static (int Minutes, decimal Days) TimeOff(int basisMinutes, decimal rate, int dailyWorkMinutes)
    {
        var minutes = (int)decimal.Round(basisMinutes * rate, 0, MidpointRounding.AwayFromZero);
        var days = decimal.Round((decimal)minutes / dailyWorkMinutes, 4, MidpointRounding.AwayFromZero);
        return (minutes, days);
    }
}
