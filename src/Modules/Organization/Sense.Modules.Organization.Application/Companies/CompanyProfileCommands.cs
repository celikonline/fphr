using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Companies;

/// <summary>
/// Updates the tenant's primary Company profile (Faz 2, TB-10 "Şirket profili" screen). Renames the
/// attached OrgUnit (Name) and sets the legal/registry + operating-hours fields together, same "primary
/// company" resolution as <see cref="GetCompanyProfileQuery"/>. Logo is a separate command
/// (<see cref="SetCompanyLogoCommand"/>) since it is set via a different flow (Documents upload).
/// </summary>
[RequiresPermission(OrganizationPermissions.CompanyProfileManageKey)]
public sealed record UpdateCompanyProfileCommand(
    string Name,
    string? RegistrationNumber,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Country,
    string? OperatingHoursStart,
    string? OperatingHoursEnd,
    IReadOnlyList<string>? WorkingDays) : ICommand<CompanyProfileDto>;

public sealed class UpdateCompanyProfileValidator : AbstractValidator<UpdateCompanyProfileCommand>
{
    public UpdateCompanyProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
        RuleFor(x => x.RegistrationNumber).MaximumLength(OrganizationLimits.RegistrationNumberMaxLength);
        RuleFor(x => x.TaxNumber).MaximumLength(OrganizationLimits.TaxNumberMaxLength);
        RuleFor(x => x.Address).MaximumLength(OrganizationLimits.AddressMaxLength);
        RuleFor(x => x.City).MaximumLength(OrganizationLimits.CityMaxLength);
        RuleFor(x => x.Country).MaximumLength(OrganizationLimits.CountryMaxLength);
        RuleFor(x => x.OperatingHoursStart).Must(BeAValidTime).WithMessage("'{PropertyName}' geçerli bir saat olmalıdır (HH:mm).");
        RuleFor(x => x.OperatingHoursEnd).Must(BeAValidTime).WithMessage("'{PropertyName}' geçerli bir saat olmalıdır (HH:mm).");
        RuleFor(x => x.WorkingDays)
            .Must(days => days is null || days.All(d => CompanyWorkingDays.Codes.Contains(d)))
            .WithMessage("Çalışma günleri yalnızca mon,tue,wed,thu,fri,sat,sun olabilir.");
    }

    private static bool BeAValidTime(string? value) => value is null || TimeOnly.TryParse(value, out _);
}

public sealed class UpdateCompanyProfileHandler(ICompanyRepository companies, IOrgUnitRepository units)
    : ICommandHandler<UpdateCompanyProfileCommand, CompanyProfileDto>
{
    public async Task<Result<CompanyProfileDto>> Handle(UpdateCompanyProfileCommand command, CancellationToken cancellationToken)
    {
        var resolved = await PrimaryCompanyResolver.ResolveAsync(companies, units, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var (company, unit) = resolved.Value;
        var renamed = unit.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        company.UpdateProfile(
            command.RegistrationNumber,
            command.TaxNumber,
            command.Address,
            command.City,
            command.Country,
            command.OperatingHoursStart is { } start ? TimeOnly.Parse(start) : null,
            command.OperatingHoursEnd is { } end ? TimeOnly.Parse(end) : null,
            command.WorkingDays);

        return CompanyProfileMapping.ToDto(company, unit.Name);
    }
}

/// <summary>Sets (or clears, with a null DocumentId) the company's logo reference; see <see cref="UpdateCompanyProfileCommand"/> remarks.</summary>
[RequiresPermission(OrganizationPermissions.CompanyProfileManageKey)]
public sealed record SetCompanyLogoCommand(Guid? LogoDocumentId, string? LogoUrl) : ICommand<CompanyProfileDto>;

public sealed class SetCompanyLogoHandler(ICompanyRepository companies, IOrgUnitRepository units)
    : ICommandHandler<SetCompanyLogoCommand, CompanyProfileDto>
{
    public async Task<Result<CompanyProfileDto>> Handle(SetCompanyLogoCommand command, CancellationToken cancellationToken)
    {
        var resolved = await PrimaryCompanyResolver.ResolveAsync(companies, units, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var (company, unit) = resolved.Value;
        company.SetLogo(command.LogoDocumentId, command.LogoUrl);

        return CompanyProfileMapping.ToDto(company, unit.Name);
    }
}
