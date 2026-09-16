using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Assets.Infrastructure;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

/// <summary>
/// Integration tests (real PostgreSQL via Testcontainers, see TestFixture) verifying IAssetLookup.HasOpenAssetsAsync
/// reflects the persisted Asset.Status - the exact signal Documents' DocumentRequirementMaterializationService reads
/// to materialize/keep the AssetHandoverForm conditional requirement in sync (roadmap #65).
/// </summary>
[Collection("PostgreSQL")]
public sealed class AssetLookupIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<AssetsDbContext>(configuration, AssetsDbContext.SchemaName);
        services.AddAssetsCoreServices();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<AssetsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task HasOpenAssetsAsync_ReflectsAssignedVsUnassignedVsRetired()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var categoryId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Guid assetId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var assets = writeScope.ServiceProvider.GetRequiredService<IAssetRepository>();

            var asset = Asset.Create(tenantId, categoryId, "MacBook Pro 14", "SN-1", null).Value;
            assets.Add(asset);
            await db.SaveChangesAsync();
            assetId = asset.Id;
        }

        using (var checkScope = ServiceProvider!.CreateScope())
        {
            var lookup = checkScope.ServiceProvider.GetRequiredService<IAssetLookup>();
            (await lookup.HasOpenAssetsAsync(tenantId, employeeId)).ShouldBeFalse();
        }

        using (var assignScope = ServiceProvider!.CreateScope())
        {
            var db = assignScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var assets = assignScope.ServiceProvider.GetRequiredService<IAssetRepository>();
            var asset = (await assets.GetByIdAsync(assetId)).Value;

            asset.Assign(employeeId, DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var checkScope = ServiceProvider!.CreateScope())
        {
            var lookup = checkScope.ServiceProvider.GetRequiredService<IAssetLookup>();
            (await lookup.HasOpenAssetsAsync(tenantId, employeeId)).ShouldBeTrue();
        }

        using (var returnScope = ServiceProvider!.CreateScope())
        {
            var db = returnScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var assets = returnScope.ServiceProvider.GetRequiredService<IAssetRepository>();
            var asset = (await assets.GetByIdAsync(assetId)).Value;

            asset.Return(DateOnly.FromDateTime(DateTime.UtcNow)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var checkScope = ServiceProvider!.CreateScope())
        {
            var lookup = checkScope.ServiceProvider.GetRequiredService<IAssetLookup>();
            (await lookup.HasOpenAssetsAsync(tenantId, employeeId)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task HasOpenAssetsAsync_WhenRetiredWithoutEverBeingAssigned_IsFalse()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var categoryId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var assets = writeScope.ServiceProvider.GetRequiredService<IAssetRepository>();

            var asset = Asset.Create(tenantId, categoryId, "Old Phone", null, null).Value;
            asset.Retire().IsSuccess.ShouldBeTrue();
            assets.Add(asset);
            await db.SaveChangesAsync();
        }

        using var checkScope = ServiceProvider!.CreateScope();
        var lookup = checkScope.ServiceProvider.GetRequiredService<IAssetLookup>();
        (await lookup.HasOpenAssetsAsync(tenantId, employeeId)).ShouldBeFalse();
    }
}
