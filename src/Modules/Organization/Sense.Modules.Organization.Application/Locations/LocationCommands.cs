using FluentValidation;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Locations;

/// <summary>Creates a location (office/store/warehouse/factory/remote + country, roadmap Faz 0 item #6).</summary>
[RequiresPermission("organization.locations.create")]
public sealed record CreateLocationCommand(string Name, LocationType Type, string CountryCode, string? Address, string? SgkIsyeriSicilNo = null) : ICommand<LocationDto>;

public sealed class CreateLocationValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.CountryCode).NotEmpty().Length(OrganizationLimits.CountryCodeLength);
        RuleFor(x => x.Address).MaximumLength(OrganizationLimits.AddressMaxLength);
        RuleFor(x => x.SgkIsyeriSicilNo).MaximumLength(OrganizationLimits.SgkIsyeriSicilNoMaxLength);
    }
}

public sealed class CreateLocationHandler(ITenantContext tenant, ILocationRepository locations) : ICommandHandler<CreateLocationCommand, LocationDto>
{
    public Task<Result<LocationDto>> Handle(CreateLocationCommand command, CancellationToken cancellationToken)
    {
        var result = Location.Create(tenant.TenantId, command.Name, command.Type, command.CountryCode, command.Address, command.SgkIsyeriSicilNo);
        if (result.IsFailure)
        {
            return Task.FromResult<Result<LocationDto>>(result.Error);
        }

        var location = result.Value;
        locations.Add(location);

        return Task.FromResult<Result<LocationDto>>(ToDto(location));
    }

    internal static LocationDto ToDto(Location l) =>
        new(l.Id, l.Name, l.Type, l.CountryCode, l.Address, l.IsActive, l.SgkIsyeriSicilNo, l.Latitude, l.Longitude, l.GeofenceRadiusMeters, l.SelfCheckInCode);
}

/// <summary>Updates a location's name/type/country/address/SGK işyeri sicil no.</summary>
[RequiresPermission("organization.locations.update")]
public sealed record UpdateLocationCommand(Guid Id, string Name, LocationType Type, string CountryCode, string? Address, string? SgkIsyeriSicilNo = null) : ICommand<LocationDto>;

public sealed class UpdateLocationValidator : AbstractValidator<UpdateLocationCommand>
{
    public UpdateLocationValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.CountryCode).NotEmpty().Length(OrganizationLimits.CountryCodeLength);
        RuleFor(x => x.Address).MaximumLength(OrganizationLimits.AddressMaxLength);
        RuleFor(x => x.SgkIsyeriSicilNo).MaximumLength(OrganizationLimits.SgkIsyeriSicilNoMaxLength);
    }
}

public sealed class UpdateLocationHandler(ILocationRepository locations) : ICommandHandler<UpdateLocationCommand, LocationDto>
{
    public async Task<Result<LocationDto>> Handle(UpdateLocationCommand command, CancellationToken cancellationToken)
    {
        var existing = await locations.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var location = existing.Value;
        var updated = location.Update(command.Name, command.Type, command.CountryCode, command.Address, command.SgkIsyeriSicilNo);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        return CreateLocationHandler.ToDto(location);
    }
}

/// <summary>
/// Sets or clears a location's GPS geofence (S-31 kabul kriteri 2): center point + optional radius the Attendance
/// module validates a self-servis GPS check-in against. Same permission as the location's other fields.
/// </summary>
[RequiresPermission("organization.locations.update")]
public sealed record SetLocationGeofenceCommand(Guid Id, double? Latitude, double? Longitude, int? RadiusMeters) : ICommand<LocationDto>;

public sealed class SetLocationGeofenceValidator : AbstractValidator<SetLocationGeofenceCommand>
{
    public SetLocationGeofenceValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x.RadiusMeters).GreaterThan(0).When(x => x.RadiusMeters is not null);
    }
}

public sealed class SetLocationGeofenceHandler(ILocationRepository locations) : ICommandHandler<SetLocationGeofenceCommand, LocationDto>
{
    public async Task<Result<LocationDto>> Handle(SetLocationGeofenceCommand command, CancellationToken cancellationToken)
    {
        var existing = await locations.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var location = existing.Value;
        var result = location.SetGeofence(command.Latitude, command.Longitude, command.RadiusMeters);
        return result.IsFailure ? result.Error : CreateLocationHandler.ToDto(location);
    }
}

/// <summary>
/// Rotates a location's QR self-servis check-in code (S-31): the previously printed QR poster stops working at once,
/// e.g. after it was photographed/shared. Same permission as the location's other fields.
/// </summary>
[RequiresPermission("organization.locations.update")]
public sealed record RegenerateLocationSelfCheckInCodeCommand(Guid Id) : ICommand<LocationDto>;

public sealed class RegenerateLocationSelfCheckInCodeValidator : AbstractValidator<RegenerateLocationSelfCheckInCodeCommand>
{
    public RegenerateLocationSelfCheckInCodeValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class RegenerateLocationSelfCheckInCodeHandler(ILocationRepository locations)
    : ICommandHandler<RegenerateLocationSelfCheckInCodeCommand, LocationDto>
{
    public async Task<Result<LocationDto>> Handle(RegenerateLocationSelfCheckInCodeCommand command, CancellationToken cancellationToken)
    {
        var existing = await locations.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        existing.Value.RegenerateSelfCheckInCode();
        return CreateLocationHandler.ToDto(existing.Value);
    }
}

/// <summary>Soft-deletes a location (IsActive=false); fails with 409 if an active org unit still references it.</summary>
[RequiresPermission("organization.locations.delete")]
public sealed record DeactivateLocationCommand(Guid Id) : ICommand;

public sealed class DeactivateLocationValidator : AbstractValidator<DeactivateLocationCommand>
{
    public DeactivateLocationValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeactivateLocationHandler(ILocationRepository locations, IOrgUnitRepository units) : ICommandHandler<DeactivateLocationCommand>
{
    public async Task<Result> Handle(DeactivateLocationCommand command, CancellationToken cancellationToken)
    {
        var existing = await locations.GetByIdAsync(command.Id, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        if (await units.HasActiveLocationReferenceAsync(command.Id, cancellationToken))
        {
            return Error.Conflict(OrganizationErrors.LocationInUse);
        }

        var result = existing.Value.Deactivate();
        return result.IsFailure ? result.Error : Result.Success();
    }
}
