namespace Sense.Modules.Attendance.Domain.AttendanceDays;

/// <summary>
/// Uyuşmazlık çipleri (11.3.6 "Çip kuralları"). A day can carry more than one flag (e.g. LateArrival + Overwork).
/// Which flags render as a UI chip is a tenant setting (ShowLateArrivalChip/ShowEarlyLeaveChip); the domain always
/// computes and stores all of them.
/// </summary>
[Flags]
public enum AttendanceAnomaly
{
    None = 0,

    /// <summary>Working day per schedule, no check-in and no check-out at all ("kimse çalışmamış gibi").</summary>
    NoData = 1 << 0,

    MissingCheckIn = 1 << 1,

    MissingCheckOut = 1 << 2,

    /// <summary>Check-in later than the schedule's start.</summary>
    LateArrival = 1 << 3,

    /// <summary>Check-out earlier than the schedule's end.</summary>
    EarlyLeave = 1 << 4,

    /// <summary>Check-out later than the schedule's end.</summary>
    LateLeave = 1 << 5,

    /// <summary>Worked minutes exceed planned minutes.</summary>
    Overwork = 1 << 6,

    /// <summary>Worked minutes fall short of planned minutes.</summary>
    Underwork = 1 << 7,

    /// <summary>Check-in/out recorded on a day the schedule marks as not working.</summary>
    UnplannedWork = 1 << 8,
}

/// <summary>How an anomaly was handled (11.3.6 "Çözüm etkisi"); resolution actions land in package A3.</summary>
public enum AttendanceResolutionState
{
    Unresolved = 0,
    Resolved = 1,
    Ignored = 2,
}

/// <summary>Where a day's check-in/out came from.</summary>
public enum AttendanceDaySource
{
    Manual = 0,
    Import = 1,
    Device = 2,
}

/// <summary>
/// Öneri butonları (11.3.6 "Öneri kuralı"): "Fazla mesai ekle" (AddOvertime), "Çalışılan saat" (SetWorkedHours),
/// "İzin talebi" (CreateLeaveRequest) and the escape hatch to dismiss a flagged day without either (Ignore).
/// </summary>
public enum AnomalyResolutionAction
{
    AddOvertime = 0,
    SetWorkedHours = 1,
    CreateLeaveRequest = 2,
    Ignore = 3,
}

/// <summary>
/// Anomaly → allowed action set (11.3.6: "Fazla çalışma → yalnız Fazla mesai ekle aktif... Az çalışma → Çalışılan
/// saat veya İzin talebi"). Ignore is always offered as a manual override; missing/unplanned-data anomalies with
/// neither Overwork nor Underwork only allow a manual correction (Çalışılan saat).
/// </summary>
public static class AnomalyResolutionAdvisor
{
    public static IReadOnlySet<AnomalyResolutionAction> AllowedActions(AttendanceAnomaly anomalies)
    {
        if (anomalies == AttendanceAnomaly.None)
        {
            return new HashSet<AnomalyResolutionAction>();
        }

        var actions = new HashSet<AnomalyResolutionAction> { AnomalyResolutionAction.Ignore };

        if ((anomalies & (AttendanceAnomaly.Overwork | AttendanceAnomaly.LateLeave)) != 0)
        {
            actions.Add(AnomalyResolutionAction.AddOvertime);
        }

        if ((anomalies & AttendanceAnomaly.Underwork) != 0)
        {
            actions.Add(AnomalyResolutionAction.SetWorkedHours);
            actions.Add(AnomalyResolutionAction.CreateLeaveRequest);
        }

        if ((anomalies & (AttendanceAnomaly.NoData | AttendanceAnomaly.MissingCheckIn | AttendanceAnomaly.MissingCheckOut | AttendanceAnomaly.UnplannedWork)) != 0)
        {
            actions.Add(AnomalyResolutionAction.SetWorkedHours);
        }

        return actions;
    }

    public static bool IsAllowed(AttendanceAnomaly anomalies, AnomalyResolutionAction action) => AllowedActions(anomalies).Contains(action);
}
