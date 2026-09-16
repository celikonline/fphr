using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Categories;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Infrastructure.Lookups;
using Sense.Modules.Assets.Infrastructure.Persistence;

namespace Sense.Modules.Assets.Infrastructure;

public static class AssetsServiceCollectionExtensions
{
    /// <summary>
    /// Services Assets needs (repos, provisioning, unit of work, cross-module IAssetLookup). Used by both the API
    /// module (AssetsModule.AddModule) and Sense.Worker (so the outbox poller and IAssetLookup consumers resolve),
    /// mirroring Expense's AddExpenseCoreServices. Assets produces AssetAssigned/AssetReturned but consumes no other
    /// module's events in this pass, so there is no matching AddAssetsIntegrationEventHandlers half (see Expense/
    /// Recruitment for the shape a package with consumers would add).
    /// </summary>
    public static IServiceCollection AddAssetsCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IAssetCategoryRepository, AssetCategoryRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetsUnitOfWork>(sp => sp.GetRequiredService<AssetsDbContext>());
        services.AddScoped<AssetCategoryProvisioningService>();
        services.AddScoped<IAssetLookup, AssetLookup>();
        return services;
    }
}
