using Sense.Modules.Organization.Contracts;

namespace Sense.Modules.OccupationalSafety.Application.Incidents;

/// <summary>
/// 5510 sayılı Kanun md.13: iş kazası, kazadan sonraki üç iş günü içinde SGK'ya bildirilir. Business days skip
/// Saturday, Sunday and the tenant's holidays (IHolidayCalendarLookup, same source Leave uses for day counting).
/// </summary>
public static class SgkNotificationDeadline
{
    public const int BusinessDays = 3;

    /// <summary>The last day of the notification window: the <see cref="BusinessDays"/>-th business day after the event.</summary>
    public static async Task<DateOnly> CalculateAsync(
        DateOnly eventDate, Func<DateOnly, Task<bool>> isHoliday, CancellationToken cancellationToken = default)
    {
        var day = eventDate;
        var counted = 0;
        while (counted < BusinessDays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            day = day.AddDays(1);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || await isHoliday(day))
            {
                continue;
            }

            counted++;
        }

        return day;
    }

    public static Task<DateOnly> CalculateAsync(
        IHolidayCalendarLookup holidays, Guid tenantId, DateOnly eventDate, CancellationToken cancellationToken = default) =>
        CalculateAsync(eventDate, day => holidays.IsHolidayAsync(tenantId, day, cancellationToken), cancellationToken);
}
