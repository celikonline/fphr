using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Workflow.Application.ApprovalRequests;

public sealed class ApprovalRequestReadAccess(
    IApprovalRequestRepository requests, IRoleAssignmentLookup roles, IPermissionService permissions) : IApprovalRequestReadAccess
{
    public async Task<bool> CanReadAsync(Guid tenantId, Guid approvalRequestId, string requestType, Guid subjectId,
        Guid? employeeId, Guid? userId, CancellationToken ct = default)
    {
        var loaded = await requests.GetByIdAsync(approvalRequestId, ct);
        if (loaded.IsFailure) return false;
        var request = loaded.Value;
        return request.TenantId == tenantId && request.SubjectId == subjectId
            && string.Equals(request.RequestType, requestType, StringComparison.Ordinal)
            && await ApprovalRequestAccess.IsPartyAsync(request, employeeId, userId, tenantId, roles, permissions, ct);
    }
}
