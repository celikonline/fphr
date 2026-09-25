namespace Sense.Modules.Attendance.Domain.Shifts;

/// <summary>
/// Planlanan günün türü (Z0.2, spec §4). <see cref="Work"/> dışındaki günlerde vardiya saat alanları hesaplamada yok
/// sayılır (hafta tatili / dinlenme / resmi tatil planı).
/// </summary>
public enum ShiftDayType
{
    Work = 0,
    WeeklyRest = 1,
    RestDay = 2,
    PublicHoliday = 3,
}
