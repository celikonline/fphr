using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Modules.Workflow.Infrastructure.Security;
using Sense.Shared.Contracts.Configuration;

namespace Sense.Modules.Workflow.Infrastructure.Messaging;

/// <summary>
/// Workflow.Contracts.IApprovalDecisionLinkIssuer: mints a decision token for the task only while it is still the
/// pending request's current step with a concrete approver, and saves the hash right away. Depends on nothing but
/// WorkflowDbContext and the token/link helpers, so Sense.Worker can resolve it without the rest of the module.
/// </summary>
public sealed class ApprovalDecisionLinkIssuer(
    WorkflowDbContext db,
    IEmailTokenGenerator tokenGenerator,
    IWorkflowFrontendLinks frontendLinks,
    TimeProvider clock) : IApprovalDecisionLinkIssuer
{
    public async Task<ApprovalDecisionLink?> IssueAsync(Guid tenantId, Guid approvalTaskId, CancellationToken ct = default)
    {
        var task = await db.ApprovalTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Id == approvalTaskId)
            .Select(t => new { t.ApprovalRequestId, t.StepNumber, t.ApproverEmployeeId, t.Status })
            .FirstOrDefaultAsync(ct);
        if (task is null || task.ApproverEmployeeId is null || task.Status != ApprovalTaskStatus.Pending)
        {
            return null;
        }

        var isCurrent = await db.ApprovalRequests.AsNoTracking().IgnoreAutoIncludes()
            .AnyAsync(r => r.TenantId == tenantId && r.Id == task.ApprovalRequestId
                && r.Status == ApprovalRequestStatus.Pending && r.CurrentStepNumber == task.StepNumber, ct);
        if (!isCurrent)
        {
            return null;
        }

        var rawToken = tokenGenerator.NewToken();
        var expiresAt = clock.GetUtcNow().UtcDateTime.Add(WorkflowLimits.EmailDecisionTokenLifetime);
        db.EmailDecisionTokens.Add(new EmailDecisionToken(approvalTaskId, tenantId, tokenGenerator.Hash(rawToken), expiresAt));
        await db.SaveChangesAsync(ct);

        return new ApprovalDecisionLink(frontendLinks.DecisionLink(rawToken), expiresAt);
    }
}

public static class WorkflowDecisionLinkServiceCollectionExtensions
{
    /// <summary>
    /// Registers IApprovalDecisionLinkIssuer and its helpers. Shared by WorkflowModule (Api) and Sense.Worker, where
    /// Notifications sends the approval e-mails; the host must already have registered WorkflowDbContext.
    /// </summary>
    public static IServiceCollection AddWorkflowDecisionLinks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IEmailTokenGenerator, EmailTokenGenerator>();
        services.AddScoped<IWorkflowFrontendLinks, WorkflowFrontendLinks>();
        services.AddOptions<WorkflowFrontendOptions>().Bind(configuration.GetSection(ConfigurationSections.Frontend));
        services.AddScoped<IApprovalDecisionLinkIssuer, ApprovalDecisionLinkIssuer>();
        return services;
    }
}
