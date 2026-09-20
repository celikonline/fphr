using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Application.Companies;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence.Seed;
using Sense.Shared.Contracts.Context;

namespace Sense.Modules.Organization.Infrastructure.Persistence;

/// <summary>
/// <see cref="ITenantOrganizationBootstrapper"/> uygulaması. Her adım "yoksa ekle" biçimindedir, dolayısıyla olay tekrar
/// teslim edilse ya da sihirbaz ve Worker aynı anda çalışsa da (bir tarafın benzersizlik ihlaliyle yeniden denemesi dışında)
/// ikinci bir Company/takvim oluşmaz. Kendi SaveChanges'ini yapar; çağıran kiracı kapsamı açmış olmak zorunda değildir.
/// </summary>
public sealed class TenantOrganizationBootstrapper(OrganizationDbContext db, ITenantContextSetter tenantSetter) : ITenantOrganizationBootstrapper
{
    public async Task EnsureAsync(Guid tenantId, string companyName, CancellationToken ct)
    {
        using var scope = tenantSetter.BeginScope(tenantId);

        var companyTypeId = await EnsureUnitTypesAsync(tenantId, ct).ConfigureAwait(false);
        var calendar = await db.HolidayCalendars.FirstOrDefaultAsync(c => c.IsSystemDefined, ct).ConfigureAwait(false)
            ?? TrHolidayCalendarSeed.AddTo(db, tenantId);

        if (!await db.Companies.AnyAsync(ct).ConfigureAwait(false))
        {
            var unit = OrgUnit.Create(tenantId, companyTypeId, parentId: null, companyName.Trim());
            if (unit.IsFailure)
            {
                throw new InvalidOperationException($"Failed to create the root company unit: {unit.Error}");
            }

            var company = Company.Create(tenantId, unit.Value.Id, calendar.Id);
            if (company.IsFailure)
            {
                throw new InvalidOperationException($"Failed to create the company row: {company.Error}");
            }

            db.OrgUnits.Add(unit.Value);
            db.Companies.Add(company.Value);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <returns>Company düzeyi birim tipinin kimliği (yoksa sistem tipleri eklenir).</returns>
    private async Task<Guid> EnsureUnitTypesAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await db.OrgUnitTypes.ToListAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return CompanyTypeId(existing);
        }

        var created = new List<OrgUnitType>();
        foreach (var result in OrgUnitTypeCatalog.CreateAll(tenantId))
        {
            if (result.IsFailure)
            {
                throw new InvalidOperationException($"Failed to create a system org unit type: {result.Error}");
            }

            created.Add(result.Value);
        }

        db.OrgUnitTypes.AddRange(created);
        return CompanyTypeId(created);
    }

    private static Guid CompanyTypeId(IEnumerable<OrgUnitType> types) =>
        types.First(t => t.Code == OrgUnitTypeCodes.Company && t.IsSystem).Id;
}
