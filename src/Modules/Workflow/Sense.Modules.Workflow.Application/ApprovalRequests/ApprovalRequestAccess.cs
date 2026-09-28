using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Workflow.Application.ApprovalRequests;

/// <summary>
/// FG-009: who is a party of one approval request - and so may read its status/step timeline and read/write its
/// comments: the requester, the concrete approver or decider of any of its tasks, a member of a task's fallback role
/// queue, or a holder of <c>workflow.request.override</c>. Everyone else must get NotFound (not Forbidden) so the
/// request's existence is not leaked. <c>workflow.inbox.view</c> deliberately grants nothing here: every employee holds it.
/// </summary>
internal static class ApprovalRequestAccess
{
    public static async Task<bool> IsPartyAsync(
        ApprovalRequest request,
        Guid? employeeId,
        Guid? userId,
        Guid tenantId,
        IRoleAssignmentLookup roleAssignments,
        IPermissionService permissions,
        CancellationToken cancellationToken)
    {
        if (employeeId is { } callerEmployeeId && callerEmployeeId != Guid.Empty)
        {
            if (request.RequesterEmployeeId == callerEmployeeId
                || request.Tasks.Any(t => t.ApproverEmployeeId == callerEmployeeId || t.DecidedByEmployeeId == callerEmployeeId))
            {
                return true;
            }

            var fallbackRoleCodes = request.Tasks
                .Select(t => t.FallbackRoleCode)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal);
            foreach (var roleCode in fallbackRoleCodes)
            {
                if (await roleAssignments.EmployeeHasRoleAsync(tenantId, callerEmployeeId, roleCode, cancellationToken))
                {
                    return true;
                }
            }
        }

        return userId is { } callerUserId
            && await permissions.HasAsync(callerUserId, WorkflowPermissions.RequestOverride.Key, cancellationToken);
    }
}
