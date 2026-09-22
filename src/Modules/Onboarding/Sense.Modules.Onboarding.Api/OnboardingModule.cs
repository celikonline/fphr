using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Contracts;
using Sense.Modules.Onboarding.Domain.Tasks;
using Sense.Modules.Onboarding.Infrastructure;
using Sense.Modules.Onboarding.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Onboarding.Api;

/// <summary>Onboarding (işe alış / işten çıkış görev listeleri) module composition root.</summary>
public sealed class OnboardingModule : IModule
{
    public string Name => OnboardingDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(OnboardingModule).Assembly,
        typeof(IOnboardingUnitOfWork).Assembly,
        typeof(OnboardingDbContext).Assembly,
        typeof(OnboardingPermissions).Assembly,
    ];

    public IEnumerable<Permission> Permissions => OnboardingPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates - same wiring as TrainingModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<OnboardingDbContext>(
            configuration, OnboardingDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<OnboardingDbContext>>();
        services.AddModuleHandlers(
            OnboardingDbContext.SchemaName,
            typeof(OnboardingTaskTemplate).Assembly, // Domain
            typeof(IOnboardingUnitOfWork).Assembly, // Application
            typeof(OnboardingDbContext).Assembly, // Infrastructure
            typeof(OnboardingPermissions).Assembly); // Contracts
        services.AddOnboardingCoreServices();
        services.AddOnboardingIntegrationEventHandlers();
    }
}
