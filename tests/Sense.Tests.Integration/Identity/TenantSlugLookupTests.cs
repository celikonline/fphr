using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>Faz 3 / Faz 9 köprüsü (ADR-0007): ITenantSlugLookup, ITenantRepository.GetBySlugAsync sarmalayıcısı.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class TenantSlugLookupTests : IdentityStackTestBase
{
    [Fact]
    public async Task FindTenantIdAsync_ActiveTenant_ReturnsId()
    {
        var tenantId = await SeedTenantAsync("slug-lookup-active");

        var lookup = Get<ITenantSlugLookup>();
        var result = await lookup.FindTenantIdAsync("slug-lookup-active", Ct);

        result.ShouldBe(tenantId);
    }

    [Fact]
    public async Task FindTenantIdAsync_UnknownOrSuspendedSlug_ReturnsNull()
    {
        var tenantId = await SeedTenantAsync("slug-lookup-suspended");

        var lookup = Get<ITenantSlugLookup>();
        (await lookup.FindTenantIdAsync("no-such-slug", Ct)).ShouldBeNull();

        var db = Get<IdentityDbContext>();
        var tenant = await db.Tenants.FindAsync([tenantId], Ct);
        tenant!.Suspend("test", Time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(Ct);

        (await lookup.FindTenantIdAsync("slug-lookup-suspended", Ct)).ShouldBeNull();
    }
}
