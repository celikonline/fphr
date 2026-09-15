using Microsoft.EntityFrameworkCore;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Infrastructure.Persistence;

namespace Sense.Modules.Workflow.Infrastructure.Lookups;

/// <summary>Implements Workflow.Contracts.IApprovalRequestLookup so other modules can read an approval request's effective status.</summary>
public sealed class ApprovalRequestLookup(WorkflowDbContext context) : IApprovalRequestLookup
{
    public async Task<ApprovalStatusDto?> GetStatusAsync(Guid tenantId, Guid approvalRequestId, CancellationToken ct = default)
    {
        var request = await context.ApprovalRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == approvalRequestId, ct);

        if (request is null)
        {
            return null;
        }

        var steps = request.Tasks
            .OrderBy(t => t.StepNumber)
            .Select(t => new ApprovalStepStatus(t.StepNumber, t.ApproverEmployeeId, t.FallbackRoleCode, t.IsRequired, t.Status.ToString()))
            .ToList();

        return new ApprovalStatusDto(
            request.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId, request.Status.ToString(),
            request.CurrentStepNumber, request.ShowCurrentApproverToRequester, steps);
    }
}
