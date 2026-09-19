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
[Trait("Category", "Integration")]
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
    public async Task CountOpenAssetsAsync_CountsOnlyThisEmployeesAssignedAssets()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var categoryId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var assets = writeScope.ServiceProvider.GetRequiredService<IAssetRepository>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var laptop = Asset.Create(tenantId, categoryId, "MacBook Pro 14", "SN-1", null).Value;
            laptop.Assign(employeeId, today).IsSuccess.ShouldBeTrue();
            assets.Add(laptop);

            var phone = Asset.Create(tenantId, categoryId, "iPhone 15", "SN-2", null).Value;
            phone.Assign(employeeId, today).IsSuccess.ShouldBeTrue();
            assets.Add(phone);

            var othersLaptop = Asset.Create(tenantId, categoryId, "ThinkPad", "SN-3", null).Value;
            othersLaptop.Assign(otherEmployeeId, today).IsSuccess.ShouldBeTrue();
            assets.Add(othersLaptop);

            await db.SaveChangesAsync();
        }

        using var checkScope = ServiceProvider!.CreateScope();
        var lookup = checkScope.ServiceProvider.GetRequiredService<IAssetLookup>();
        (await lookup.CountOpenAssetsAsync(tenantId, employeeId)).ShouldBe(2);
        (await lookup.CountOpenAssetsAsync(tenantId, otherEmployeeId)).ShouldBe(1);
        (await lookup.CountOpenAssetsAsync(tenantId, Guid.NewGuid())).ShouldBe(0);
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

    [Fact]
    public async Task IsSerialNumberAssignedElsewhereAsync_OnlyMatchesOtherAssignedAssets_CaseInsensitively()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var categoryId = Guid.NewGuid();
        Guid assignedId;
        Guid spareId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var repo = writeScope.ServiceProvider.GetRequiredService<IAssetRepository>();
            var assigned = Asset.Create(tenantId, categoryId, "Laptop A", "SN-DUP", null).Value;
            assigned.Assign(Guid.NewGuid(), new DateOnly(2026, 9, 1));
            var spare = Asset.Create(tenantId, categoryId, "Laptop B", "sn-dup", null).Value;
            repo.Add(assigned);
            repo.Add(spare);
            await db.SaveChangesAsync();
            assignedId = assigned.Id;
            spareId = spare.Id;
        }

        using var checkScope = ServiceProvider!.CreateScope();
        var checkRepo = checkScope.ServiceProvider.GetRequiredService<IAssetRepository>();
        (await checkRepo.IsSerialNumberAssignedElsewhereAsync("SN-DUP", spareId)).ShouldBeTrue();
        (await checkRepo.IsSerialNumberAssignedElsewhereAsync("SN-DUP", assignedId)).ShouldBeFalse();
        (await checkRepo.IsSerialNumberAssignedElsewhereAsync("SN-OTHER", spareId)).ShouldBeFalse();
    }

    [Fact]
    public async Task HasUnreturnedTerminationAuditAsync_FindsAnAlreadyWrittenAuditEvent()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var assetId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var integrationEvent = new Sense.Modules.Assets.Contracts.Events.AssetUnreturnedAtTermination(tenantId, assetId, employeeId);
            db.OutboxMessages.Add(new Sense.Shared.Infrastructure.Persistence.Outbox.OutboxMessage
            {
                Id = integrationEvent.EventId,
                TenantId = tenantId,
                Type = Sense.Shared.Infrastructure.Persistence.Outbox.EventTypeRegistry.NameOf(integrationEvent.GetType()),
                Payload = System.Text.Json.JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                OccurredAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var checkScope = ServiceProvider!.CreateScope();
        var repo = checkScope.ServiceProvider.GetRequiredService<IAssetRepository>();
        (await repo.HasUnreturnedTerminationAuditAsync(tenantId, assetId, employeeId)).ShouldBeTrue();
        (await repo.HasUnreturnedTerminationAuditAsync(tenantId, assetId, Guid.NewGuid())).ShouldBeFalse();
    }
}
