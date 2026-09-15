using FluentValidation;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.HolidayCalendars;

/// <summary>Creates a tenant-custom holiday calendar (the system TR calendar is seeded, not created through this command - see Sense.Api.SeedData.DataSeeder).</summary>
[RequiresPermission("organization.holiday_calendars.create")]
public sealed record CreateHolidayCalendarCommand(string Name, string CountryCode) : ICommand<HolidayCalendarDto>;

public sealed class CreateHolidayCalendarValidator : AbstractValidator<CreateHolidayCalendarCommand>
{
    public CreateHolidayCalendarValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
        RuleFor(x => x.CountryCode).NotEmpty().MaximumLength(OrganizationLimits.CountryCodeMaxLength);
    }
}

public sealed class CreateHolidayCalendarHandler(ITenantContext tenant, IHolidayCalendarRepository calendars)
    : ICommandHandler<CreateHolidayCalendarCommand, HolidayCalendarDto>
{
    public Task<Result<HolidayCalendarDto>> Handle(CreateHolidayCalendarCommand command, CancellationToken cancellationToken)
    {
        var result = HolidayCalendar.Create(tenant.TenantId, command.Name, command.CountryCode, isSystemDefined: false);
        if (result.IsFailure)
        {
            return Task.FromResult(Result.Failure<HolidayCalendarDto>(result.Error));
        }

        var calendar = result.Value;
        calendars.Add(calendar);

        return Task.FromResult(Result.Success(new HolidayCalendarDto(calendar.Id, calendar.Name, calendar.CountryCode, calendar.IsSystemDefined)));
    }
}

/// <summary>Adds one entry (a specific holiday) to an existing calendar.</summary>
[RequiresPermission("organization.holiday_calendars.update")]
public sealed record AddHolidayCalendarEntryCommand(Guid HolidayCalendarId, DateOnly Date, string Name, bool IsRecurringAnnually)
    : ICommand<HolidayCalendarEntryDto>;

public sealed class AddHolidayCalendarEntryValidator : AbstractValidator<AddHolidayCalendarEntryCommand>
{
    public AddHolidayCalendarEntryValidator()
    {
        RuleFor(x => x.HolidayCalendarId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class AddHolidayCalendarEntryHandler(IHolidayCalendarRepository calendars, IHolidayCalendarEntryRepository entries)
    : ICommandHandler<AddHolidayCalendarEntryCommand, HolidayCalendarEntryDto>
{
    public async Task<Result<HolidayCalendarEntryDto>> Handle(AddHolidayCalendarEntryCommand command, CancellationToken cancellationToken)
    {
        var calendar = await calendars.GetByIdAsync(command.HolidayCalendarId, cancellationToken);
        if (calendar.IsFailure)
        {
            return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
        }

        var result = HolidayCalendarEntry.Create(
            calendar.Value.TenantId, command.HolidayCalendarId, command.Date, command.Name, command.IsRecurringAnnually);
        if (result.IsFailure)
        {
            return result.Error;
        }

        entries.Add(result.Value);

        return new HolidayCalendarEntryDto(
            result.Value.Id, result.Value.HolidayCalendarId, result.Value.Date, result.Value.Name, result.Value.IsRecurringAnnually);
    }
}
