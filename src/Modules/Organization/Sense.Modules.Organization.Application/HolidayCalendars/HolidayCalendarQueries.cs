using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Shared.Contracts.Context;
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

/// <summary>
/// Holidays within [today, today + days] of the effective calendar: the given company's, else the current user's
/// employee's company (unit parents walked up to the Company level), else the tenant default / system TR calendar.
/// </summary>
[RequiresPermission(OrganizationPermissions.HolidaysViewKey)]
public sealed record GetUpcomingHolidaysQuery(int Days = GetUpcomingHolidaysQuery.DefaultDays, Guid? CompanyId = null) : IQuery<IReadOnlyList<UpcomingHolidayDto>>
{
    public const int DefaultDays = 90;
    public const int MaxDays = 366;
}

public sealed class GetUpcomingHolidaysValidator : AbstractValidator<GetUpcomingHolidaysQuery>
{
    public GetUpcomingHolidaysValidator() =>
        RuleFor(x => x.Days).InclusiveBetween(0, GetUpcomingHolidaysQuery.MaxDays).WithMessage(OrganizationErrors.HolidayDaysOutOfRange);
}

public sealed class GetUpcomingHolidaysHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    ICompanyRepository companies,
    IHolidayCalendarLookup calendarLookup,
    IEmployeeLookup employeeLookup,
    TimeProvider clock) : IQueryHandler<GetUpcomingHolidaysQuery, IReadOnlyList<UpcomingHolidayDto>>
{
    public async Task<Result<IReadOnlyList<UpcomingHolidayDto>>> Handle(GetUpcomingHolidaysQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId is { } companyId && (await companies.GetByIdAsync(companyId, cancellationToken)).IsFailure)
        {
            return Error.NotFound(OrganizationErrors.CompanyNotFound);
        }

        Guid? orgUnitId = null;
        if (query.CompanyId is null && currentUser.EmployeeId is { } employeeId)
        {
            orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, employeeId, cancellationToken);
        }

        var calendar = await calendarLookup.ResolveCalendarAsync(tenant.TenantId, query.CompanyId, orgUnitId, cancellationToken);
        if (calendar is null)
        {
            return Result.Success<IReadOnlyList<UpcomingHolidayDto>>([]);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var holidays = await calendarLookup.GetHolidaysAsync(tenant.TenantId, calendar.Id, today, today.AddDays(query.Days), cancellationToken);

        IReadOnlyList<UpcomingHolidayDto> dtos = holidays
            .Select(h => new UpcomingHolidayDto(h.Date, h.Name, calendar.Id, calendar.Name))
            .ToList();
        return Result.Success(dtos);
    }
}
