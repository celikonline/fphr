using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Application.Tasks;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Onboarding.Infrastructure;

public static class OnboardingServiceCollectionExtensions
{
    /// <summary>Repositories + unit of work; used by the API module and Sense.Worker (mirrors AddTrainingCoreServices).</summary>
    public static IServiceCollection AddOnboardingCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IOnboardingUnitOfWork>(sp => sp.GetRequiredService<OnboardingDbContext>());
        services.AddScoped<IOnboardingTaskTemplateRepository, OnboardingTaskTemplateRepository>();
        services.AddScoped<IEmployeeOnboardingTaskRepository, EmployeeOnboardingTaskRepository>();
        return services;
    }

    /// <summary>EmployeeHired / EmployeeTerminated → onboarding / offboarding checklists (same shape as AddDocumentsIntegrationEventHandlers).</summary>
    public static IServiceCollection AddOnboardingIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<EmployeeHired>, EmployeeHiredOnboardingHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeTerminated>, EmployeeTerminatedOffboardingHandler>();
        return services;
    }
}
