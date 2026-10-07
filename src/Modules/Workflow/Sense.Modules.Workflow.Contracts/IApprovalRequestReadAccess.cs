namespace Sense.Modules.Workflow.Contracts;

/// <summary>Record read access for a party of the matching approval request. Grants no write or decision authority.</summary>
public interface IApprovalRequestReadAccess
{
    Task<bool> CanReadAsync(Guid tenantId, Guid approvalRequestId, string requestType, Guid subjectId,
        Guid? employeeId, Guid? userId, CancellationToken ct = default);
}
