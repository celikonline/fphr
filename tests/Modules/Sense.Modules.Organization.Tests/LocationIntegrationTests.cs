using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Integration test (real PostgreSQL via Testcontainers, see TestFixture): creating a Location and
/// wiring it to an OrgUnit via the real FK added by this item (roadmap Faz 0 item #6 - OrgUnit.LocationId
/// was reserved with no FK by item #4; this migration adds the Location table and the constraint).
/// Mirrors OrgUnitIntegrationTests's style: goes directly through OrganizationDbContext rather than
/// Dispatcher.Send(...), since TestFixture's tenant/user helpers don't yet persist real Identity rows.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LocationIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<OrganizationDbContext>(configuration, OrganizationDbContext.SchemaName);
    }

    [Fact]
    public async Task CreateLocation_AndAssignToOrgUnit_PersistsAndIsTenantIsolated()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var location = Location.Create(tenantId, "İstanbul HQ", LocationType.Office, "TR", "Maslak, İstanbul").Value;
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        // Seed a hierarchy level and an OrgUnit, then wire it to the location via the real FK.
        var typeResults = OrgUnitTypeCatalog.CreateAll(tenantId);
        foreach (var result in typeResults)
        {
            result.IsSuccess.ShouldBeTrue();
            db.OrgUnitTypes.Add(result.Value);
        }

        await db.SaveChangesAsync();

        var companyTypeId = typeResults.Single(r => r.Value.Code == OrgUnitTypeCodes.Company).Value.Id;
        var unit = OrgUnit.Create(tenantId, companyTypeId, parentId: null, name: "Headquarters").Value;
        unit.AssignLocation(location.Id);
        db.OrgUnits.Add(unit);
        await db.SaveChangesAsync();

        // Assert: re-read through a fresh query and confirm the FK persisted.
        var reloadedUnit = await db.OrgUnits.AsNoTracking().SingleAsync(u => u.Id == unit.Id);
        reloadedUnit.LocationId.ShouldBe(location.Id);

        var reloadedLocation = await db.Locations.AsNoTracking().SingleAsync(l => l.Id == location.Id);
        reloadedLocation.Name.ShouldBe("İstanbul HQ");
        reloadedLocation.Type.ShouldBe(LocationType.Office);
        reloadedLocation.CountryCode.ShouldBe("TR");
        reloadedLocation.IsActive.ShouldBeTrue();

        // Tenant isolation: switching to a different tenant must see none of this data (ModuleDbContext's
        // global query filter, not an explicit Where clause).
        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            var visibleToOtherTenant = await db.Locations.AnyAsync(l => l.Id == location.Id);
            visibleToOtherTenant.ShouldBeFalse();
        }
    }

    /// <summary>senseik-010/F-03: SgkIsyeriSicilNo round-trips through the EF migration/mapping, and ILocationLookup.GetSgkIsyeriSicilNoAsync surfaces it for Payroll's APHB/e-Bildirge preparation.</summary>
    [Fact]
    public async Task CreateLocation_WithSgkIsyeriSicilNo_RoundTripsAndIsResolvedByLookup()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var withSicilNo = Location.Create(tenantId, "İstanbul HQ", LocationType.Office, "TR", "Maslak", "1071234.567-8.9-0").Value;
        var withoutSicilNo = Location.Create(tenantId, "Remote", LocationType.Remote, "TR", null).Value;
        db.Locations.AddRange(withSicilNo, withoutSicilNo);
        await db.SaveChangesAsync();

        var reloaded = await db.Locations.AsNoTracking().SingleAsync(l => l.Id == withSicilNo.Id);
        reloaded.SgkIsyeriSicilNo.ShouldBe("1071234.567-8.9-0");

        var lookup = new LocationLookup(db);
        var result = await lookup.GetSgkIsyeriSicilNoAsync(tenantId, [withSicilNo.Id, withoutSicilNo.Id, Guid.NewGuid()]);

        result.Count.ShouldBe(1);
        result[withSicilNo.Id].ShouldBe("1071234.567-8.9-0");
        result.ContainsKey(withoutSicilNo.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateLocation_TwiceInARow_SecondCallFails()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var location = Location.Create(tenantId, "Ankara Office", LocationType.Office, "TR", null).Value;
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var first = location.Deactivate();
        first.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var reloaded = await db.Locations.SingleAsync(l => l.Id == location.Id);
        var second = reloaded.Deactivate();
        second.IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// S-19 (Faz 2 yazar, Faz 9 sonraki tur tüketir): <see cref="LocationLookup.GetNamesAsync"/> includes
    /// inactive locations (a closed career posting may still reference one), omits ids from other tenants,
    /// and simply skips ids with no match instead of throwing.
    /// </summary>
    [Fact]
    public async Task GetNamesAsync_IncludesInactive_ExcludesOtherTenantAndUnknownIds()
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        Location active;
        Location inactive;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            active = Location.Create(tenantId, "İstanbul HQ", LocationType.Office, "TR", null).Value;
            inactive = Location.Create(tenantId, "Eski Şube", LocationType.Office, "TR", null).Value;
            inactive.Deactivate();
            db.Locations.AddRange(active, inactive);
            await db.SaveChangesAsync();
        }

        Location otherTenantLocation;
        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            otherTenantLocation = Location.Create(otherTenantId, "Başka Kiracı", LocationType.Office, "TR", null).Value;
            db.Locations.Add(otherTenantLocation);
            await db.SaveChangesAsync();
        }

        var lookup = new LocationLookup(db);
        var unknownId = Guid.NewGuid();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var names = await lookup.GetNamesAsync(tenantId, [active.Id, inactive.Id, otherTenantLocation.Id, unknownId]);

            names.Count.ShouldBe(2);
            names[active.Id].ShouldBe("İstanbul HQ");
            names[inactive.Id].ShouldBe("Eski Şube");
            names.ContainsKey(otherTenantLocation.Id).ShouldBeFalse();
            names.ContainsKey(unknownId).ShouldBeFalse();
        }
    }
}
