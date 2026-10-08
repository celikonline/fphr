using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Infrastructure.Jobs;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Domain;
using Sense.Modules.Reporting.Infrastructure.Mailer;
using Sense.Modules.Reporting.Infrastructure.Persistence;

namespace Sense.Modules.Reporting.Infrastructure;

public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddReportingCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IReportExecutionRepository, ReportExecutionRepository>();
        services.AddScoped<IReportingUnitOfWork>(sp => sp.GetRequiredService<ReportingDbContext>());
        services.AddScoped<IReportMailer, ReportMailer>();
        services.AddScoped<IReportProvider, CustomReportProvider>();
        services.AddScoped<ReportGenerationContext>();
        services.AddScoped<IReportGenerationContext>(sp => sp.GetRequiredService<ReportGenerationContext>());
        services.AddScoped<IScheduledJobHandler, ReportGenerationJob>();
        return services;
    }
}
