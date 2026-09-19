namespace Sense.Modules.Leave.Domain.Absences;

/// <summary>
/// senseik-036 md.7: 4857 md.25/I-b gives the employer a right to terminate for health reasons once a
/// non-work-related illness absence exceeds the employee's notice period (4857 md.17, 2-8 hafta by seniority)
/// plus six weeks. Leave has no cross-module port to read an employee's notice period yet (see the senseik-036
/// handoff), so <see cref="MinimumDays"/> alone (the six-week floor, ihbar süresi hariç) is a simplified,
/// informational lower bound - never a legal determination, mirroring the existing "leave:absences.thresholdAlert"
/// convention (4857 md.25/II-g) of a caveated info-only signal rather than a hard rule.
/// </summary>
public static class SickLeaveAbsenceThreshold
{
    public const decimal MinimumWeeks = 6m;
    private const int DaysPerWeek = 7;

    public const decimal MinimumDays = MinimumWeeks * DaysPerWeek;

    /// <summary>True once <paramref name="sickDaysUsedInWindow"/> (a trailing 12-month Sick-type usage total) reaches the six-week floor.</summary>
    public static bool IsReached(decimal sickDaysUsedInWindow) => sickDaysUsedInWindow >= MinimumDays;
}
