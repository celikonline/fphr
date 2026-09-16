using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
