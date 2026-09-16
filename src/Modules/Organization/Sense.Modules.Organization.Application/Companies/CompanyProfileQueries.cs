using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Companies;

/// <summary>
/// The tenant's primary company profile (Faz 2, TB-10 "Şirket profili" screen: one profile per tenant,
/// mirrors the pre-existing web mock's `getCompanyProfile()` shape). "Primary" = the Company attached to
/// the root Company-level org unit (ParentId null); Faz 0's seed creates exactly one per tenant. If a
/// tenant somehow has none (root unit not yet a Company row - should not happen post-seed) or several
/// root Company rows (future multi-legal-entity groups), the earliest-created one wins; multi-company
/// profile selection is out of scope for this screen.
/// </summary>
[RequiresPermission("organization.units.read")]
public sealed record GetCompanyProfileQuery : IQuery<CompanyProfileDto>;

public sealed class GetCompanyProfileHandler(ICompanyRepository companies, IOrgUnitRepository units)
    : IQueryHandler<GetCompanyProfileQuery, CompanyProfileDto>
{
    public async Task<Result<CompanyProfileDto>> Handle(GetCompanyProfileQuery query, CancellationToken cancellationToken)
    {
        var resolved = await PrimaryCompanyResolver.ResolveAsync(companies, units, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var (company, unit) = resolved.Value;
        return CompanyProfileMapping.ToDto(company, unit.Name);
    }
}

/// <summary>Resolves "the" tenant's primary Company row + its OrgUnit for the singleton profile screen (see <see cref="GetCompanyProfileQuery"/> remarks).</summary>
internal static class PrimaryCompanyResolver
{
    public static async Task<Result<(Company Company, OrgUnit Unit)>> ResolveAsync(
        ICompanyRepository companies, IOrgUnitRepository units, CancellationToken cancellationToken)
    {
        var all = await companies.ListAsync(cancellationToken);
        if (all.Count == 0)
        {
            return Error.NotFound(OrganizationErrors.CompanyNotFound);
        }

        var unitList = await units.ListAllAsync(activeOnly: false, cancellationToken);
        var unitsById = unitList.ToDictionary(u => u.Id, u => u);

        var primary = all
            .Where(c => unitsById.ContainsKey(c.OrgUnitId))
            .OrderBy(c => unitsById[c.OrgUnitId].ParentId is null ? 0 : 1)
            .ThenBy(c => c.CreatedAt)
            .FirstOrDefault();

        return primary is null
            ? Error.NotFound(OrganizationErrors.CompanyNotFound)
            : (primary, unitsById[primary.OrgUnitId]);
    }
}

internal static class CompanyProfileMapping
{
    public static CompanyProfileDto ToDto(Company company, string name) => new(
        company.Id,
        company.OrgUnitId,
        name,
        company.RegistrationNumber,
        company.TaxNumber,
        company.Address,
        company.City,
        company.Country,
        company.OperatingHoursStart?.ToString("HH\\:mm"),
        company.OperatingHoursEnd?.ToString("HH\\:mm"),
        company.WorkingDays,
        company.LogoDocumentId,
        company.LogoUrl,
        company.ModifiedDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(company.ModifiedDate.Value, DateTimeKind.Utc)) : new DateTimeOffset(DateTime.SpecifyKind(company.CreatedAt, DateTimeKind.Utc)));
}
