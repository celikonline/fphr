using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Onboarding.Application;
using Sense.Modules.Onboarding.Domain;
using Sense.Modules.Onboarding.Infrastructure.Persistence;

namespace Sense.Modules.Onboarding.Infrastructure;

public static class OnboardingServiceCollectionExtensions
{
    /// <summary>Repositories + unit of work; used by the API module and Sense.Worker (mirrors AddTrainingCoreServices).</summary>
    public static IServiceCollection AddOnboardingCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IOnboardingUnitOfWork>(sp => sp.GetRequiredService<OnboardingDbContext>());
        services.AddScoped<IOnboardingTaskTemplateRepository, OnboardingTaskTemplateRepository>();
        services.AddScoped<IEmployeeOnboardingTaskRepository, EmployeeOnboardingTaskRepository>();
        services.AddScoped<IOnboardingBuddyAssignmentRepository, OnboardingBuddyAssignmentRepository>();
        services.AddScoped<IOnboardingCategoryRepository, OnboardingCategoryRepository>();
        services.AddScoped<IOnboardingTemplateSetRepository, OnboardingTemplateSetRepository>();
        return services;
    }
}
