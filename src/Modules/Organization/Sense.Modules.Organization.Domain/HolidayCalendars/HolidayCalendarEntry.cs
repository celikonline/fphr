using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.HolidayCalendars;

/// <summary>
/// One holiday row within a <see cref="HolidayCalendar"/>.
/// <b>Design decision (roadmap Faz 0 item #5)</b>: a single <see cref="Date"/> (DateOnly) column plus
/// <see cref="IsRecurringAnnually"/> covers both shapes the spec called out, instead of carrying two
/// parallel field sets ("Date" and "MonthDay"):
///   - Fixed-date national holidays (Jan 1, Apr 23, May 1, May 19, Jul 15, Aug 30, Oct 29) are seeded
///     once with IsRecurringAnnually=true. <see cref="Date"/>'s Year is then just an arbitrary anchor
///     (the seeder uses a fixed anchor year) - only Month/Day are meaningful, and <see cref="MatchesDate"/>
///     compares month+day only, so the same row matches every year without re-seeding.
///   - Religious holidays (Ramazan/Kurban Bayramı) shift every year against the Gregorian calendar
///     (lunar calendar) and are seeded with IsRecurringAnnually=false and an exact Date for one specific
///     year. Nothing in this codebase computes the lunar dates - see
///     src/Sense.Api/SeedData/holidays/tr-official-holidays.json, which seeds only a couple of
///     upcoming known dates per religious holiday and flags them with a "note" that they need a fresh
///     row every year (periodic manual/Platform-admin refresh), never a computed projection.
/// </summary>
public sealed class HolidayCalendarEntry : TenantAggregateRoot<Guid>, IAuditLogged
{
    private HolidayCalendarEntry()
    {
    }

    private HolidayCalendarEntry(Guid id, Guid tenantId, Guid holidayCalendarId, DateOnly date, string name, bool isRecurringAnnually)
        : base(id, tenantId)
    {
        HolidayCalendarId = holidayCalendarId;
        Date = date;
        Name = name;
        IsRecurringAnnually = isRecurringAnnually;
    }

    /// <summary>The calendar this entry belongs to (same-module, same-schema FK - see OrganizationDbContext.HolidayCalendarEntryConfiguration).</summary>
    public Guid HolidayCalendarId { get; private set; }

    /// <summary>See class doc: exact date for a non-recurring entry, or an arbitrary-year anchor (month/day only matter) for a recurring one.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Display name, e.g. "Cumhuriyet Bayramı".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>True if this entry falls on the same Gregorian month/day every year (fixed-date national holidays); false for a one-off/shifting date (religious holidays, a specific company closure day).</summary>
    public bool IsRecurringAnnually { get; private set; }

    public static Result<HolidayCalendarEntry> Create(
        Guid tenantId, Guid holidayCalendarId, DateOnly date, string name, bool isRecurringAnnually, Guid? id = null)
    {
        if (holidayCalendarId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.HolidayCalendarEntryCalendarIdRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.HolidayCalendarEntryNameRequired);
        }

        var entry = new HolidayCalendarEntry(
            id ?? Guid.CreateVersion7(),
            tenantId,
            holidayCalendarId,
            date,
            Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength),
            isRecurringAnnually);

        entry.Raise(new HolidayCalendarEntryAdded(entry.Id, tenantId, holidayCalendarId, entry.Date, entry.Name, isRecurringAnnually));
        return entry;
    }

    /// <summary>True if this entry falls on <paramref name="candidate"/> - exact match when non-recurring, month/day match (any year) when recurring.</summary>
    public bool MatchesDate(DateOnly candidate) =>
        IsRecurringAnnually
            ? Date.Month == candidate.Month && Date.Day == candidate.Day
            : Date == candidate;
}

public sealed record HolidayCalendarEntryAdded(
    Guid HolidayCalendarEntryId, Guid TenantId, Guid HolidayCalendarId, DateOnly Date, string Name, bool IsRecurringAnnually) : DomainEvent;
