using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Identity.Api;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Context;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Erişim ayarları uçtan uca: gerçek Postgres (Testcontainers) + tam Identity modülü (AuthorizationBehaviour dahil
/// pipeline) üzerinden Dispatcher.Send/Query. API controller'ları bu handler'lara ince bir HTTP sarmalayıcıdır;
/// asıl davranış (yetki, kalıcılık, kiracı izolasyonu) burada, gerçek veritabanına karşı doğrulanır.
/// </summary>
[Collection("PostgreSQL")]
public sealed class AccessSettingsIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // TestFixture zaten IConfiguration'ı kaydetti (ConnectionStrings:Database dahil); Identity modülünün
        // kendi ayarları (Identity/Auth/Security/Frontend/Email) burada olmasa da varsayılanlarla bağlanır.
        using var bootstrap = services.BuildServiceProvider();
        var configuration = bootstrap.GetRequiredService<IConfiguration>();
        new IdentityModule().AddModule(services, configuration);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AccessSettings_RoundTrip_ThroughDispatcher_PersistsToPostgres()
    {
        var tenantId = await SeedTenantAsync("acme");
        SetPlatformAdminUser(tenantId);

        var current = await ExecuteQueryAsync(new GetAccessSettingsQuery());
        current.IsSuccess.ShouldBeTrue();
        current.Value.ShouldBe(AccessSettingsDto.From(TenantAccessSettings.Default));

        var update = new UpdateAccessSettingsCommand(
            ShowOrgInfo: false, ShowUpcomingLeaves: false, ShowUpcomingBirthdays: true, ShowEventCalendar: false,
            ShowFiles: false, ShowAssets: true, ShowWorkPhoneNumbers: false, ShowOrgChart: true,
            ShowCustomFields: true, ShowCalendarMenu: false, ShowAllLeavesInCalendar: true);

        var updateResult = await ExecuteCommandAsync(update);
        updateResult.IsSuccess.ShouldBeTrue();

        var reread = await ExecuteQueryAsync(new GetAccessSettingsQuery());
        reread.IsSuccess.ShouldBeTrue();
        reread.Value.ShouldBe(update.ToDto());

        // Değişiklik gerçekten Postgres'e (jsonb kolonuna) yazılmış: taze bir DbContext örneğiyle doğrula.
        var freshDb = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        var storedTenant = await freshDb.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, TestContext.Current.CancellationToken);
        storedTenant.AccessSettings.ShouldBe(update.ToDto().ToDomain());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AccessSettings_TenantIsolation_UpdatingTenantA_NeverLeaksToTenantB()
    {
        var tenantAId = await SeedTenantAsync("tenant-a");
        var tenantBId = await SeedTenantAsync("tenant-b");

        SetPlatformAdminUser(tenantAId);
        var updateA = new UpdateAccessSettingsCommand(
            ShowOrgInfo: false, ShowUpcomingLeaves: false, ShowUpcomingBirthdays: false, ShowEventCalendar: false,
            ShowFiles: false, ShowAssets: false, ShowWorkPhoneNumbers: false, ShowOrgChart: false,
            ShowCustomFields: true, ShowCalendarMenu: false, ShowAllLeavesInCalendar: true);
        (await ExecuteCommandAsync(updateA)).IsSuccess.ShouldBeTrue();

        // Kiracı B'ye geçildiğinde, A'nın "Kaydet" ettiği değerler görünmemeli (varsayılanlarda kalmalı).
        SetPlatformAdminUser(tenantBId);
        var tenantBSettings = await ExecuteQueryAsync(new GetAccessSettingsQuery());
        tenantBSettings.IsSuccess.ShouldBeTrue();
        tenantBSettings.Value.ShouldBe(AccessSettingsDto.From(TenantAccessSettings.Default));

        // Kiracı A'ya dönüldüğünde kendi değişikliği hâlâ yerinde.
        SetPlatformAdminUser(tenantAId);
        var tenantASettings = await ExecuteQueryAsync(new GetAccessSettingsQuery());
        tenantASettings.Value.ShouldBe(updateA.ToDto());

        // EF'in global "Tenant" query filter'ı da doğrudan doğrulanır: filtresiz sorguda iki satır, farklı jsonb.
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        var allTenants = await db.Tenants.IgnoreQueryFilters([IdentityDbContext.TenantFilter]).AsNoTracking()
            .Where(t => t.Id == tenantAId || t.Id == tenantBId)
            .ToListAsync(TestContext.Current.CancellationToken);
        allTenants.Single(t => t.Id == tenantAId).AccessSettings.ShouldBe(updateA.ToDto().ToDomain());
        allTenants.Single(t => t.Id == tenantBId).AccessSettings.ShouldBe(TenantAccessSettings.Default);
    }

    private async Task<Guid> SeedTenantAsync(string slug)
    {
        var tenants = ServiceProvider!.GetRequiredService<ITenantRepository>();
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        var tenant = Tenant.Create(slug, slug, DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        using (TenantContextSetter!.BeginScope(tenant.Id, tenant.Slug))
        {
            tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        return tenant.Id;
    }

    /// <summary>Platform admin: AuthorizationBehaviour izin denetimini atlar, rol/izin seed'i olmadan handler'ı test etmeyi sağlar.</summary>
    private void SetPlatformAdminUser(Guid tenantId)
    {
        var accessor = (CurrentUserAccessor)CurrentUser!;
        accessor.Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "admin@test.local", DisplayName: "Admin",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: true,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
}
