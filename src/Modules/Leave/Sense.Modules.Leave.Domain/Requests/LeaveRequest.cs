using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Requests;

public enum LeaveRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Withdrawn = 3,
    Cancelled = 4,
}

public enum LeaveRequestSource
{
    SelfService = 0,
    OnBehalf = 1,
    Import = 2,
    Attendance = 3,
    Resubmission = 4,
}

/// <summary>
/// Leave request lifecycle (11.3.5 "Durum makineleri"): Pending → Approved (Workflow ApprovalCompleted) | Rejected
/// (ApprovalRejected) | Withdrawn (requester). Pending → Pending on edit (<see cref="RevisionNo"/>++, a new approval
/// request replaces the old one). Rejected/Withdrawn/Cancelled are terminal.
/// </summary>
public sealed class LeaveRequest : TenantAggregateRoot<Guid>, IAuditLogged
{
    private LeaveRequest()
    {
    }

    private LeaveRequest(Guid tenantId, Guid employeeId, LeaveRequestSource source, DateTime submittedAt, string? ip, string? userAgent)
        : base(Guid.CreateVersion7(), tenantId)
    {
        EmployeeId = employeeId;
        Source = source;
        Status = LeaveRequestStatus.Pending;
        SubmittedAt = submittedAt;
        SubmittedIpAddress = ip;
        SubmittedUserAgent = userAgent;
    }

    public Guid EmployeeId { get; private set; }

    public Guid LeaveTypeId { get; private set; }

    public LeaveTypeCode LeaveTypeCodeSnapshot { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public decimal CalculatedDays { get; private set; }

    public string? Description { get; private set; }

    public LeaveRequestStatus Status { get; private set; }

    public LeaveRequestSource Source { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public int RevisionNo { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public string? SubmittedIpAddress { get; private set; }

    public string? SubmittedUserAgent { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public bool IsActive => Status is LeaveRequestStatus.Pending or LeaveRequestStatus.Approved;

    public static Result<LeaveRequest> Submit(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, LeaveTypeCode code, DateOnly start, DateOnly end, decimal days, string? description,
        LeaveRequestSource source, DateTime submittedAt, string? ip, string? userAgent)
    {
        var request = new LeaveRequest(tenantId, employeeId, source, submittedAt, ip, userAgent);
        var applied = request.ApplyDetails(leaveTypeId, code, start, end, days, description);
        return applied.IsFailure ? applied.Error : request;
    }

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    /// <summary>UpdatePendingLeaveRequest: new values, RevisionNo++, the caller starts a fresh approval request.</summary>
    public Result UpdatePending(Guid leaveTypeId, LeaveTypeCode code, DateOnly start, DateOnly end, decimal days, string? description)
    {
        if (Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        var applied = ApplyDetails(leaveTypeId, code, start, end, days, description);
        if (applied.IsFailure)
        {
            return applied;
        }

        RevisionNo++;
        ApprovalRequestId = null;
        return Result.Success();
    }

    public Result Approve(Guid approvalRequestId, DateTime at) => Decide(approvalRequestId, LeaveRequestStatus.Approved, at);

    /// <summary>"Onay sürecine dahil edilmeden onaylansın": a manager/HR entry approved at creation, without a Workflow approval request.</summary>
    public Result ApproveWithoutWorkflow(DateTime at)
    {
        if (Status != LeaveRequestStatus.Pending || ApprovalRequestId is not null)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        Status = LeaveRequestStatus.Approved;
        DecidedAt = at;
        return Result.Success();
    }

    public Result Reject(Guid approvalRequestId, DateTime at) => Decide(approvalRequestId, LeaveRequestStatus.Rejected, at);

    public Result Withdraw(DateTime at)
    {
        if (Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        Status = LeaveRequestStatus.Withdrawn;
        DecidedAt = at;
        return Result.Success();
    }

    public bool Overlaps(DateOnly start, DateOnly end) => StartDate <= end && start <= EndDate;

    private Result Decide(Guid approvalRequestId, LeaveRequestStatus status, DateTime at)
    {
        if (Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        // A decision for a superseded revision's approval request must not change the edited request.
        if (ApprovalRequestId != approvalRequestId)
        {
            return Error.Conflict(LeaveDomainErrors.RequestApprovalMismatch);
        }

        Status = status;
        DecidedAt = at;
        return Result.Success();
    }

    private Result ApplyDetails(Guid leaveTypeId, LeaveTypeCode code, DateOnly start, DateOnly end, decimal days, string? description)
    {
        if (end < start)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        if (days <= 0m)
        {
            return Error.Validation(LeaveDomainErrors.RequestZeroDays);
        }

        LeaveTypeId = leaveTypeId;
        LeaveTypeCodeSnapshot = code;
        StartDate = start;
        EndDate = end;
        CalculatedDays = decimal.Round(days, 4);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }
}
