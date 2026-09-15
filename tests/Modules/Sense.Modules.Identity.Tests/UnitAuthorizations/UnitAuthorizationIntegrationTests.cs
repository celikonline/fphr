using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Identity.Domain.UnitAuthorizations;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.UnitAuthorizations;

/// <summary>
/// Integration test (real PostgreSQL via Testcontainers, see TestFixture): a UnitAuthorization grant
/// round-trips through the real EF Core mapping/migration, and - the specific business rule this item
/// is required to prove - a grant created under one tenant is never visible when querying under a
/// different tenant, via ModuleDbContext's global query filter (not an explicit Where clause). Goes
/// directly through IdentityDbContext rather than Dispatcher.Send(...), same rationale as
/// Sense.Modules.Organization.Tests.OrgUnitIntegrationTests: TestFixture's tenant/user helpers are
/// documented placeholders that do not yet persist real Identity rows, so a full command-pipeline test
/// (permission checks, IOrgUnitLookup validation included) is not yet possible.
/// </summary>
[Collection("PostgreSQL")]
public sealed class UnitAuthorizationIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName);
    }

    [Fact]
    public async Task Grant_PersistsAndIsTenantIsolated()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var grantedByUserId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var created = UnitAuthorization.Grant(tenantId, orgUnitId, userId, canViewSalaryAndPayments: true, canViewUnitReports: true, grantedByUserId, now);
            created.IsSuccess.ShouldBeTrue();
            db.UnitAuthorizations.Add(created.Value);
            await db.SaveChangesAsync();
        }

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var reloaded = await db.UnitAuthorizations.AsNoTracking().SingleAsync(a => a.OrgUnitId == orgUnitId && a.UserId == userId);
            reloaded.TenantId.ShouldBe(tenantId);
            reloaded.CanViewSalaryAndPayments.ShouldBeTrue();
            reloaded.CanViewUnitReports.ShouldBeTrue();
            reloaded.GrantedByUserId.ShouldBe(grantedByUserId);
            reloaded.IsActive.ShouldBeTrue();
        }

        // Tenant isolation: a different tenant must see none of this data (global query filter).
        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            var visibleToOtherTenant = await db.UnitAuthorizations.AnyAsync(a => a.OrgUnitId == orgUnitId && a.UserId == userId);
            visibleToOtherTenant.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Revoke_IsSoftDelete_AndAllowsReGrantAfterwards()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var grantedByUserId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var grant = UnitAuthorization.Grant(tenantId, orgUnitId, userId, true, false, grantedByUserId, now).Value;
        db.UnitAuthorizations.Add(grant);
        await db.SaveChangesAsync();

        grant.Revoke(now.AddDays(1)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var revoked = await db.UnitAuthorizations.AsNoTracking().SingleAsync(a => a.Id == grant.Id);
        revoked.IsActive.ShouldBeFalse();
        revoked.RevokedAt.ShouldBe(now.AddDays(1));

        // A new grant for the same (OrgUnit, User) is allowed once the previous one is revoked - the
        // partial unique index only constrains active rows.
        var regrant = UnitAuthorization.Grant(tenantId, orgUnitId, userId, false, true, grantedByUserId, now.AddDays(2));
        regrant.IsSuccess.ShouldBeTrue();
        db.UnitAuthorizations.Add(regrant.Value);
        await db.SaveChangesAsync();

        var activeCount = await db.UnitAuthorizations.CountAsync(a => a.OrgUnitId == orgUnitId && a.UserId == userId && a.IsActive);
        activeCount.ShouldBe(1);
    }
}
