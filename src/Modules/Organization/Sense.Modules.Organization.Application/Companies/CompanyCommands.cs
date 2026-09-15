using FluentValidation;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Companies;

/// <summary>
/// "Şirket ekle" (Ad + Tatil takvimi): creates a root Company-level org unit and its Company row atomically
/// (same DbContext, one unit of work).
/// </summary>
[RequiresPermission(OrganizationPermissions.CompaniesCreateKey)]
public sealed record CreateCompanyCommand(string Name, Guid? HolidayCalendarId) : ICommand<CompanyDto>;

public sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(OrganizationLimits.NameMaxLength);
}

public sealed class CreateCompanyHandler(
    ITenantContext tenant,
    IOrgUnitTypeRepository types,
    IOrgUnitRepository units,
    ICompanyRepository companies,
    IHolidayCalendarRepository calendars) : ICommandHandler<CreateCompanyCommand, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(CreateCompanyCommand command, CancellationToken cancellationToken)
    {
        var companyType = await types.GetByCodeAsync(OrgUnitTypeCodes.Company, cancellationToken);
        if (companyType is null || !companyType.IsActive)
        {
            return Error.Rule(OrganizationErrors.CompanyLevelMissing);
        }

        string? calendarName = null;
        if (command.HolidayCalendarId is { } calendarId)
        {
            var calendar = await calendars.GetByIdAsync(calendarId, cancellationToken);
            if (calendar.IsFailure)
            {
                return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
            }

            calendarName = calendar.Value.Name;
        }

        var unit = OrgUnit.Create(tenant.TenantId, companyType.Id, parentId: null, command.Name);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        var company = Company.Create(tenant.TenantId, unit.Value.Id, command.HolidayCalendarId);
        if (company.IsFailure)
        {
            return company.Error;
        }

        units.Add(unit.Value);
        companies.Add(company.Value);

        return CompanyMapping.ToDto(company.Value, unit.Value.Name, calendarName);
    }
}

/// <summary>
/// Assigns (or clears, with a null HolidayCalendarId) a holiday calendar to a Company; it applies to the company's
/// whole sub-hierarchy (resolved via IHolidayCalendarLookup).
/// </summary>
[RequiresPermission("organization.holiday_calendars.update")]
public sealed record AssignCompanyHolidayCalendarCommand(Guid CompanyId, Guid? HolidayCalendarId) : ICommand<CompanyDto>;

public sealed class AssignCompanyHolidayCalendarValidator : AbstractValidator<AssignCompanyHolidayCalendarCommand>
{
    public AssignCompanyHolidayCalendarValidator() => RuleFor(x => x.CompanyId).NotEmpty();
}

public sealed class AssignCompanyHolidayCalendarHandler(ICompanyRepository companies, IOrgUnitRepository units, IHolidayCalendarRepository calendars)
    : ICommandHandler<AssignCompanyHolidayCalendarCommand, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(AssignCompanyHolidayCalendarCommand command, CancellationToken cancellationToken)
    {
        var company = await companies.GetByIdAsync(command.CompanyId, cancellationToken);
        if (company.IsFailure)
        {
            return company.Error;
        }

        string? calendarName = null;
        if (command.HolidayCalendarId is { } calendarId)
        {
            var calendar = await calendars.GetByIdAsync(calendarId, cancellationToken);
            if (calendar.IsFailure)
            {
                return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
            }

            calendarName = calendar.Value.Name;
        }

        company.Value.SetHolidayCalendar(command.HolidayCalendarId);

        var unit = await units.GetByIdAsync(company.Value.OrgUnitId, cancellationToken);
        return CompanyMapping.ToDto(company.Value, unit.IsSuccess ? unit.Value.Name : string.Empty, calendarName);
    }
}

internal static class CompanyMapping
{
    public static CompanyDto ToDto(Company company, string name, string? calendarName) =>
        new(company.Id, company.OrgUnitId, name, company.HolidayCalendarId, calendarName, company.JointEmploymentGroupCode);
}
