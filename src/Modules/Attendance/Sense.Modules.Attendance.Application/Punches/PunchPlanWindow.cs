namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Bir puantaj gününün planı (okutma → gün türetmesinin girdisi, spec §5.1). <see cref="Start"/>/<see cref="End"/> boşsa gün planlı
/// ama çalışma günü değildir (hafta tatili); hiç plan yoksa (ne atama ne çizelge) çağıran <c>null</c> <see cref="PunchPlanWindow"/> geçer.
/// <see cref="End"/> ≤ <see cref="Start"/> gece vardiyasıdır; <see cref="StartsPreviousDay"/> vardiyanın bir önceki takvim gününde başladığını söyler.
/// </summary>
public sealed record PunchPlanWindow(TimeOnly? Start, TimeOnly? End, int BreakMinutes, bool StartsPreviousDay = false)
{
    /// <summary>Planlı ama çalışılmayan gün (hafta tatili / çizelgede kapalı gün).</summary>
    public static PunchPlanWindow NoWindow { get; } = new(null, null, 0);

    public bool IsWorkingDay => Start is not null && End is not null;

    /// <summary><paramref name="workDate"/> gününün vardiya başlangıç/bitiş anları (yerel); çalışma günü değilse null.</summary>
    public (DateTime Start, DateTime End)? ShiftBounds(DateOnly workDate)
    {
        if (Start is not { } start || End is not { } end)
        {
            return null;
        }

        var shiftStart = workDate.ToDateTime(start).AddDays(StartsPreviousDay ? -1 : 0);
        var shiftEnd = DateOnly.FromDateTime(shiftStart).ToDateTime(end).AddDays(end <= start ? 1 : 0);
        return (shiftStart, shiftEnd);
    }
}
