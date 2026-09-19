using Sense.Modules.Announcements.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

/// <summary>
/// Pure calendar math for the company calendar merge (senseik-027): range validation and birthday-in-range
/// derivation. Kept free of I/O so the "hangi doğum günü hangi tarihte görünür" logic is unit-testable without
/// mocking a repository (mirrors Leave.Application.Calendar.LeaveCalendar's ValidateRange/IsMasked/Build helpers).
/// </summary>
public static class CompanyEventCalendar
{
    public static Result ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return Error.Validation(AnnouncementsErrors.CalendarInvalidRange);
        }

        return to.DayNumber - from.DayNumber + 1 > CompanyEventLimits.CalendarMaxRangeDays
            ? Error.Validation(AnnouncementsErrors.CalendarRangeTooLarge, ("max", CompanyEventLimits.CalendarMaxRangeDays))
            : Result.Success();
    }

    /// <summary>
    /// Every employee whose birthday (month/day, year ignored) falls within [from, to]. A range can span at most
    /// two calendar years (month-view padding around New Year's), so each birth date is checked against both years
    /// once - a birthday cannot occur twice in a range this short (max 62 days), so no de-duplication is needed.
    /// 29 Feb birthdays are celebrated on 28 Feb in non-leap years, mirroring Employee's own BirthdayCalculator.
    /// </summary>
    public static IReadOnlyList<(Guid EmployeeId, DateOnly Date)> DeriveBirthdays(
        IReadOnlyDictionary<Guid, DateOnly> birthDatesByEmployeeId, DateOnly from, DateOnly to)
    {
        var entries = new List<(Guid, DateOnly)>();
        foreach (var (employeeId, birthDate) in birthDatesByEmployeeId)
        {
            foreach (var year in DistinctYears(from, to))
            {
                var occurrence = InYear(birthDate, year);
                if (occurrence >= from && occurrence <= to)
                {
                    entries.Add((employeeId, occurrence));
                }
            }
        }

        return entries;
    }

    private static IEnumerable<int> DistinctYears(DateOnly from, DateOnly to) =>
        from.Year == to.Year ? [from.Year] : [from.Year, to.Year];

    // 29 Şubat doğumlular artık olmayan yıllarda 28 Şubat'ta kutlanır (Employee.Application.BirthdayCalculator ile aynı kural).
    private static DateOnly InYear(DateOnly birthDate, int year) =>
        new(year, birthDate.Month, Math.Min(birthDate.Day, DateTime.DaysInMonth(year, birthDate.Month)));
}
