using Sense.Modules.Organization.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.HolidayCalendars;

/// <summary>Lists holiday calendars for the current tenant (the seeded system TR calendar plus any tenant-authored custom ones).</summary>
[RequiresPermission("organization.holiday_calendars.read")]
public sealed record ListHolidayCalendarsQuery : IQuery<IReadOnlyList<HolidayCalendarDto>>;

public sealed class ListHolidayCalendarsHandler(IHolidayCalendarRepository calendars)
    : IQueryHandler<ListHolidayCalendarsQuery, IReadOnlyList<HolidayCalendarDto>>
{
    public async Task<Result<IReadOnlyList<HolidayCalendarDto>>> Handle(ListHolidayCalendarsQuery query, CancellationToken cancellationToken)
    {
        var list = await calendars.ListAsync(cancellationToken);
        IReadOnlyList<HolidayCalendarDto> dtos = list
            .Select(c => new HolidayCalendarDto(c.Id, c.Name, c.CountryCode, c.IsSystemDefined))
            .ToList();

        // Result<TValue>'s implicit conversion is not applicable when TValue is an interface type, so
        // an IReadOnlyList<T> result must go through Result.Success(...) explicitly (same reasoning as
        // ListOrgUnitsHandler).
        return Result.Success(dtos);
    }
}

/// <summary>Lists entries for one calendar, ordered by date.</summary>
[RequiresPermission("organization.holiday_calendars.read")]
public sealed record ListHolidayCalendarEntriesQuery(Guid HolidayCalendarId) : IQuery<IReadOnlyList<HolidayCalendarEntryDto>>;

public sealed class ListHolidayCalendarEntriesHandler(IHolidayCalendarEntryRepository entries)
    : IQueryHandler<ListHolidayCalendarEntriesQuery, IReadOnlyList<HolidayCalendarEntryDto>>
{
    public async Task<Result<IReadOnlyList<HolidayCalendarEntryDto>>> Handle(ListHolidayCalendarEntriesQuery query, CancellationToken cancellationToken)
    {
        var list = await entries.ListByCalendarAsync(query.HolidayCalendarId, cancellationToken);
        IReadOnlyList<HolidayCalendarEntryDto> dtos = list
            .Select(e => new HolidayCalendarEntryDto(e.Id, e.HolidayCalendarId, e.Date, e.Name, e.IsRecurringAnnually))
            .ToList();

        return Result.Success(dtos);
    }
}
