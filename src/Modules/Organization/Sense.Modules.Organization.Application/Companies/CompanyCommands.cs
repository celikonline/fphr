using FluentValidation;
using Sense.Modules.Organization.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Companies;

/// <summary>
/// Assigns (or clears, with a null HolidayCalendarId) a holiday calendar to a Company - roadmap Faz 0
/// item #5's "Şirket eklerken tatil takvimi seçilir" UX, exposed as its own endpoint (PUT
/// /organization/companies/{id}/holiday-calendar) rather than folded into a general Company update
/// command, since item #4 did not add one yet (Company is currently seed-only; see Company.cs).
/// </summary>
[RequiresPermission("organization.holiday_calendars.update")]
public sealed record AssignCompanyHolidayCalendarCommand(Guid CompanyId, Guid? HolidayCalendarId) : ICommand<CompanyDto>;

public sealed class AssignCompanyHolidayCalendarValidator : AbstractValidator<AssignCompanyHolidayCalendarCommand>
{
    public AssignCompanyHolidayCalendarValidator() => RuleFor(x => x.CompanyId).NotEmpty();
}

public sealed class AssignCompanyHolidayCalendarHandler(ICompanyRepository companies, IHolidayCalendarRepository calendars)
    : ICommandHandler<AssignCompanyHolidayCalendarCommand, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(AssignCompanyHolidayCalendarCommand command, CancellationToken cancellationToken)
    {
        var company = await companies.GetByIdAsync(command.CompanyId, cancellationToken);
        if (company.IsFailure)
        {
            return company.Error;
        }

        if (command.HolidayCalendarId is { } calendarId)
        {
            var calendar = await calendars.GetByIdAsync(calendarId, cancellationToken);
            if (calendar.IsFailure)
            {
                return Error.NotFound(OrganizationErrors.HolidayCalendarNotFound);
            }
        }

        company.Value.SetHolidayCalendar(command.HolidayCalendarId);

        return new CompanyDto(company.Value.Id, company.Value.OrgUnitId, company.Value.HolidayCalendarId, company.Value.JointEmploymentGroupCode);
    }
}
