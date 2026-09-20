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
public sealed record AddHolidayCalendarEntryCommand(Guid HolidayCalendarId, DateOnly Date, string Name, bool IsRecurringAnnually, bool IsHalfDay = false)
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
            calendar.Value.TenantId, command.HolidayCalendarId, command.Date, command.Name, command.IsRecurringAnnually, command.IsHalfDay);
        if (result.IsFailure)
        {
            return result.Error;
        }

        entries.Add(result.Value);

        return HolidayCalendarMapping.ToDto(result.Value);
    }
}

/// <summary>Renames a tenant-custom holiday calendar; the seeded system calendar is read-only.</summary>
[RequiresPermission("organization.holiday_calendars.update")]
public sealed record UpdateHolidayCalendarCommand(Guid Id, string Name) : ICommand<HolidayCalendarDto>;

public sealed class UpdateHolidayCalendarValidator : AbstractValidator<UpdateHolidayCalendarCommand>
{
    public UpdateHolidayCalendarValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class UpdateHolidayCalendarHandler(IHolidayCalendarRepository calendars)
    : ICommandHandler<UpdateHolidayCalendarCommand, HolidayCalendarDto>
{
    public async Task<Result<HolidayCalendarDto>> Handle(UpdateHolidayCalendarCommand command, CancellationToken cancellationToken)
    {
        var calendar = await calendars.GetByIdAsync(command.Id, cancellationToken);
        if (calendar.IsFailure)
        {
            return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
        }

        var renamed = calendar.Value.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        return new HolidayCalendarDto(calendar.Value.Id, calendar.Value.Name, calendar.Value.CountryCode, calendar.Value.IsSystemDefined);
    }
}

/// <summary>Deletes a tenant-custom holiday calendar with its entries; the system calendar, or one assigned to a company, cannot be deleted.</summary>
[RequiresPermission("organization.holiday_calendars.delete")]
public sealed record DeleteHolidayCalendarCommand(Guid Id) : ICommand;

public sealed class DeleteHolidayCalendarValidator : AbstractValidator<DeleteHolidayCalendarCommand>
{
    public DeleteHolidayCalendarValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeleteHolidayCalendarHandler(IHolidayCalendarRepository calendars) : ICommandHandler<DeleteHolidayCalendarCommand>
{
    public async Task<Result> Handle(DeleteHolidayCalendarCommand command, CancellationToken cancellationToken)
    {
        var calendar = await calendars.GetByIdAsync(command.Id, cancellationToken);
        if (calendar.IsFailure)
        {
            return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
        }

        if (calendar.Value.IsSystemDefined)
        {
            return Error.Rule(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
        }

        if (await calendars.IsAssignedToCompanyAsync(command.Id, cancellationToken))
        {
            return Error.Conflict(OrganizationErrors.HolidayCalendarInUse);
        }

        calendars.Remove(calendar.Value);
        return Result.Success();
    }
}

/// <summary>Edits one holiday entry (date, name, yearly recurrence, half-day flag) of a tenant-custom calendar.</summary>
[RequiresPermission("organization.holiday_calendars.update")]
public sealed record UpdateHolidayCalendarEntryCommand(
    Guid HolidayCalendarId, Guid EntryId, DateOnly Date, string Name, bool IsRecurringAnnually, bool IsHalfDay)
    : ICommand<HolidayCalendarEntryDto>;

public sealed class UpdateHolidayCalendarEntryValidator : AbstractValidator<UpdateHolidayCalendarEntryCommand>
{
    public UpdateHolidayCalendarEntryValidator()
    {
        RuleFor(x => x.HolidayCalendarId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
    }
}

public sealed class UpdateHolidayCalendarEntryHandler(IHolidayCalendarRepository calendars, IHolidayCalendarEntryRepository entries)
    : ICommandHandler<UpdateHolidayCalendarEntryCommand, HolidayCalendarEntryDto>
{
    public async Task<Result<HolidayCalendarEntryDto>> Handle(UpdateHolidayCalendarEntryCommand command, CancellationToken cancellationToken)
    {
        var entry = await HolidayCalendarEntryLoader.LoadEditableAsync(calendars, entries, command.HolidayCalendarId, command.EntryId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        var updated = entry.Value.Update(command.Date, command.Name, command.IsRecurringAnnually, command.IsHalfDay);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        return HolidayCalendarMapping.ToDto(entry.Value);
    }
}

/// <summary>Deletes one holiday entry from a tenant-custom calendar.</summary>
[RequiresPermission("organization.holiday_calendars.update")]
public sealed record DeleteHolidayCalendarEntryCommand(Guid HolidayCalendarId, Guid EntryId) : ICommand;

public sealed class DeleteHolidayCalendarEntryValidator : AbstractValidator<DeleteHolidayCalendarEntryCommand>
{
    public DeleteHolidayCalendarEntryValidator()
    {
        RuleFor(x => x.HolidayCalendarId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
    }
}

public sealed class DeleteHolidayCalendarEntryHandler(IHolidayCalendarRepository calendars, IHolidayCalendarEntryRepository entries)
    : ICommandHandler<DeleteHolidayCalendarEntryCommand>
{
    public async Task<Result> Handle(DeleteHolidayCalendarEntryCommand command, CancellationToken cancellationToken)
    {
        var entry = await HolidayCalendarEntryLoader.LoadEditableAsync(calendars, entries, command.HolidayCalendarId, command.EntryId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        entries.Remove(entry.Value);
        return Result.Success();
    }
}

internal static class HolidayCalendarMapping
{
    public static HolidayCalendarEntryDto ToDto(HolidayCalendarEntry entry) =>
        new(entry.Id, entry.HolidayCalendarId, entry.Date, entry.Name, entry.IsRecurringAnnually, entry.IsHalfDay);
}

internal static class HolidayCalendarEntryLoader
{
    /// <summary>Loads an entry for edit/delete: 404 when the calendar or entry is missing, 422 for the read-only system calendar.</summary>
    public static async Task<Result<HolidayCalendarEntry>> LoadEditableAsync(
        IHolidayCalendarRepository calendars, IHolidayCalendarEntryRepository entries, Guid calendarId, Guid entryId, CancellationToken cancellationToken)
    {
        var calendar = await calendars.GetByIdAsync(calendarId, cancellationToken);
        if (calendar.IsFailure)
        {
            return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
        }

        var entry = await entries.FindAsync(calendarId, entryId, cancellationToken);
        if (entry is null)
        {
            return Error.NotFound(OrganizationErrors.HolidayCalendarEntryNotFound);
        }

        return calendar.Value.IsSystemDefined
            ? Error.Rule(OrganizationDomainErrors.HolidayCalendarSystemImmutable)
            : entry;
    }
}
