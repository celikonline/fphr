namespace Sense.Modules.Organization.Application.HolidayCalendars;

/// <summary>A holiday calendar (the seeded system TR calendar, or a tenant-authored custom one).</summary>
public sealed record HolidayCalendarDto(Guid Id, string Name, string CountryCode, bool IsSystemDefined);

/// <summary>One holiday entry within a calendar. See HolidayCalendarEntry's doc comment for the Date/IsRecurringAnnually design.</summary>
public sealed record HolidayCalendarEntryDto(Guid Id, Guid HolidayCalendarId, DateOnly Date, string Name, bool IsRecurringAnnually);
