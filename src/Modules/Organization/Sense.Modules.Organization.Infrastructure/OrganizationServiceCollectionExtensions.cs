using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Application.Companies;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Organization.Infrastructure;

public static class OrganizationServiceCollectionExtensions
{
    /// <summary>
    /// Yeni kiracının Organization varsayılanlarını (birim tipleri, kök şirket, TR tatil takvimi) kuran servis. API host'u yalnız bunu
    /// kaydeder (sihirbazın "hazırla" komutu); <see cref="AddOrganizationIntegrationEventHandlers"/> Worker içindir.
    /// </summary>
    public static IServiceCollection AddOrganizationTenantBootstrap(this IServiceCollection services)
    {
        services.AddScoped<ITenantOrganizationBootstrapper, TenantOrganizationBootstrapper>();
        return services;
    }

    /// <summary>
    /// Organization'ın tükettiği entegrasyon olayları (Identity'nin <see cref="TenantProvisioned"/>'ı). Worker, Organization'ın
    /// Application assembly'sini taramadığı için işleyiciler burada açıkça kaydedilir (Documents/Leave ile aynı kalıp).
    /// </summary>
    public static IServiceCollection AddOrganizationIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddOrganizationTenantBootstrap();
        services.AddScoped<IIntegrationEventHandler<TenantProvisioned>, TenantProvisionedHandler>();
        return services;
    }
}
