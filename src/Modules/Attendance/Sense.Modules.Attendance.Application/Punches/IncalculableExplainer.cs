using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>Derives the "why" of an incalculable day from its punches and the plan resolved for it (shared by the timesheet and daily lists).</summary>
public static class IncalculableExplainer
{
    public static IncalculableReason Explain(IReadOnlyList<Punch> dayPunches, PlannedDay? plan)
    {
        var reason = DayFromPunches.Build(dayPunches, plan).Reason;
        if (reason != IncalculableReason.None)
        {
            return reason;
        }

        return plan?.IsAmbiguous == true ? IncalculableReason.AmbiguousShift : IncalculableReason.NightShiftOverlap;
    }

    /// <summary>camelCase name as serialized for enums elsewhere in the API (e.g. <c>channelConflict</c>).</summary>
    public static string Code(IncalculableReason reason) => char.ToLowerInvariant(reason.ToString()[0]) + reason.ToString()[1..];
}
