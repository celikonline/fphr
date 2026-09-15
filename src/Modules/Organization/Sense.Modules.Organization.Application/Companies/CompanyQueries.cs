using Sense.Modules.Organization.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Companies;

/// <summary>Companies of the current tenant whose Company-level org unit is active, ordered by name.</summary>
[RequiresPermission("organization.units.read")]
public sealed record ListCompaniesQuery : IQuery<IReadOnlyList<CompanyDto>>;

public sealed class ListCompaniesHandler(ICompanyRepository companies, IOrgUnitRepository units, IHolidayCalendarRepository calendars)
    : IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>>
{
    public async Task<Result<IReadOnlyList<CompanyDto>>> Handle(ListCompaniesQuery query, CancellationToken cancellationToken)
    {
        var all = await companies.ListAsync(cancellationToken);
        var unitNames = (await units.ListAllAsync(activeOnly: true, cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        var calendarIds = all.Where(c => c.HolidayCalendarId is not null).Select(c => c.HolidayCalendarId!.Value).Distinct().ToList();
        var calendarNames = (await calendars.ListByIdsAsync(calendarIds, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);

        IReadOnlyList<CompanyDto> dtos = all
            .Where(c => unitNames.ContainsKey(c.OrgUnitId))
            .Select(c => CompanyMapping.ToDto(
                c,
                unitNames[c.OrgUnitId],
                c.HolidayCalendarId is { } id && calendarNames.TryGetValue(id, out var name) ? name : null))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Result.Success(dtos);
    }
}
