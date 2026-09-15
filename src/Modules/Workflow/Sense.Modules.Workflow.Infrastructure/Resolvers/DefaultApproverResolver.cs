using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Infrastructure.Resolvers;

/// <summary>
/// Default implementation of Application.IApproverResolver. Resolution order: org-unit approval policy assignment
/// -&gt; direct manager -&gt; fallback role queue (see IApproverResolver's doc comment). A resolver "failure" is only a
/// truly unexpected error (e.g. an unreachable dependency) - every step of the chain below always produces a valid
/// result, so the normal "nothing resolved yet" paths are not failures.
/// </summary>
public sealed class DefaultApproverResolver(
    IOrgApprovalPolicyResolver orgApprovalPolicyResolver,
    IEmployeeLookup employeeLookup,
    IApprovalPolicyRepository approvalPolicyRepository) : IApproverResolver
{
    /// <summary>
    /// Mirrors Sense.Modules.Identity.Domain.Roles.SystemRoleCodes.HR - Workflow.Infrastructure cannot reference
    /// Identity.Domain (modules talk only via *.Contracts, and Identity.Contracts does not re-expose this constant),
    /// so the literal is duplicated here.
    /// </summary>
    private const string FallbackApproverRoleCode = "HR";

    public async Task<Result<ApproverResolution>> ResolveAsync(Guid tenantId, string requestType, Guid requesterEmployeeId, CancellationToken ct = default)
    {
        try
        {
            var orgUnitId = await employeeLookup.GetOrgUnitIdAsync(tenantId, requesterEmployeeId, ct);
            if (orgUnitId is { } unitId)
            {
                var policyResolution = await orgApprovalPolicyResolver.ResolveAsync(tenantId, unitId, requestType, ct);
                if (policyResolution is not null)
                {
                    var policy = await approvalPolicyRepository.FindByIdAsync(policyResolution.ApprovalPolicyId, ct);
                    if (policy is { IsActive: true })
                    {
                        var steps = policy.Steps
                            .Select(s => new ApprovalTaskSeed(s.StepNumber, s.ApproverEmployeeId, null, s.IsRequired))
                            .ToArray();
                        return new ApproverResolution(steps, policy.Id, policy.Name, policyResolution.SourceOrgUnitId, policyResolution.Inherited);
                    }

                    // Stale/deleted/inactive policy id - tolerate and fall through to the manager/role-queue default (do not throw or fail).
                }
            }

            var managerEmployeeId = await employeeLookup.GetManagerEmployeeIdAsync(tenantId, requesterEmployeeId, ct);
            if (managerEmployeeId is { } managerId)
            {
                return new ApproverResolution([new ApprovalTaskSeed(1, managerId, null, true)], null, null, null, false);
            }

            // TODO: once a Workflow-specific tenant-settings override exists (TenantSettings.Workflow.FallbackApproverRoleCode),
            // read it here instead of always defaulting to the built-in HR system role.
            return new ApproverResolution([new ApprovalTaskSeed(1, null, FallbackApproverRoleCode, true)], null, null, null, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.Failure(ErrorCodes.InternalError, (nameof(Exception), ex.Message));
        }
    }
}
