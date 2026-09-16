using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Application.JobRequisitions;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Recruitment.Infrastructure;

public static class RecruitmentServiceCollectionExtensions
{
    /// <summary>Recruitment's SaveChanges port for its integration event handlers (see <see cref="IRecruitmentUnitOfWork"/>).</summary>
    public static IServiceCollection AddRecruitmentCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IRecruitmentUnitOfWork>(sp => sp.GetRequiredService<RecruitmentDbContext>());
        return services;
    }

    /// <summary>
    /// Registered explicitly (not the whole Application assembly) so API-only handler dependencies stay out, mirroring
    /// Leave's AddLeaveIntegrationEventHandlers. Consumes Workflow's "Requisition" approval decisions (R1, roadmap #87).
    /// </summary>
    public static IServiceCollection AddRecruitmentIntegrationEventHandlers(this IServiceCollection services)
    {
        services.AddScoped<IIntegrationEventHandler<ApprovalCompleted>, RequisitionApprovalCompletedHandler>();
        services.AddScoped<IIntegrationEventHandler<ApprovalRejected>, RequisitionApprovalRejectedHandler>();
        return services;
    }
}
