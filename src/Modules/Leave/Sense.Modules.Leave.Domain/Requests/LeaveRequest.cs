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
/// request replaces the old one). Pending/Approved → Cancelled (authorized user, <see cref="Cancel"/>). Rejected/Withdrawn/Cancelled are terminal.
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

    /// <summary>Set together with <see cref="EndTime"/> for an Hourly request (single day, StartDate == EndDate); null otherwise.</summary>
    public TimeOnly? StartTime { get; private set; }

    public TimeOnly? EndTime { get; private set; }

    /// <summary>Set for a HalfDay request (single day); mutually exclusive with <see cref="StartTime"/>/<see cref="EndTime"/>.</summary>
    public HalfDayPeriod? HalfDayPeriod { get; private set; }

    public string? Description { get; private set; }

    public LeaveRequestStatus Status { get; private set; }

    public LeaveRequestSource Source { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public int RevisionNo { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public string? SubmittedIpAddress { get; private set; }

    public string? SubmittedUserAgent { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    /// <summary>The rejected request this one re-submits (<see cref="LeaveRequestSource.Resubmission"/>); the rejected record stays unchanged.</summary>
    public Guid? PreviousRequestId { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public bool IsActive => Status is LeaveRequestStatus.Pending or LeaveRequestStatus.Approved;

    public static Result<LeaveRequest> Submit(
        Guid tenantId, Guid employeeId, Guid leaveTypeId, LeaveTypeCode code, DateOnly start, DateOnly end, decimal days, string? description,
        LeaveRequestSource source, DateTime submittedAt, string? ip, string? userAgent, Guid? previousRequestId = null,
        TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        var request = new LeaveRequest(tenantId, employeeId, source, submittedAt, ip, userAgent) { PreviousRequestId = previousRequestId };
        var applied = request.ApplyDetails(leaveTypeId, code, start, end, days, description, startTime, endTime, halfDayPeriod);
        return applied.IsFailure ? applied.Error : request;
    }

    /// <summary>
    /// Decision #16: an approved leave is never edited - an authorized user cancels it (any dates) and a new request is opened.
    /// Pending requests can be cancelled too (the caller cancels the Workflow approval). Rejected/Withdrawn/Cancelled cannot.
    /// </summary>
    public Result Cancel(string reason, Guid? userId, DateTime at)
    {
        if (Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved))
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotCancellable);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > LeaveLimits.CancellationReasonMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.RequestCancellationReasonRequired);
        }

        Status = LeaveRequestStatus.Cancelled;
        CancelledAt = at;
        CancellationReason = reason.Trim();
        CancelledByUserId = userId;
        return Result.Success();
    }

    /// <summary>Rule set reassignment: an open request follows its type's counterpart (same Code) in the employee's new rule set.</summary>
    public void MoveToLeaveType(Guid leaveTypeId) => LeaveTypeId = leaveTypeId;

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    /// <summary>UpdatePendingLeaveRequest: new values, RevisionNo++, the caller starts a fresh approval request.</summary>
    public Result UpdatePending(
        Guid leaveTypeId, LeaveTypeCode code, DateOnly start, DateOnly end, decimal days, string? description,
        TimeOnly? startTime = null, TimeOnly? endTime = null, HalfDayPeriod? halfDayPeriod = null)
    {
        if (Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        var applied = ApplyDetails(leaveTypeId, code, start, end, days, description, startTime, endTime, halfDayPeriod);
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

    private Result ApplyDetails(
        Guid leaveTypeId, LeaveTypeCode code, DateOnly start, DateOnly end, decimal days, string? description,
        TimeOnly? startTime, TimeOnly? endTime, HalfDayPeriod? halfDayPeriod)
    {
        if (end < start)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidRange);
        }

        if (days <= 0m)
        {
            return Error.Validation(LeaveDomainErrors.RequestZeroDays);
        }

        var hasHours = startTime is not null || endTime is not null;
        if (hasHours && halfDayPeriod is not null)
        {
            return Error.Validation(LeaveDomainErrors.RequestPartialDayTimeInvalid);
        }

        if (hasHours && (startTime is null || endTime is null || endTime <= startTime))
        {
            return Error.Validation(LeaveDomainErrors.RequestPartialDayTimeInvalid);
        }

        if ((hasHours || halfDayPeriod is not null) && start != end)
        {
            return Error.Validation(LeaveDomainErrors.RequestPartialDayRequiresSingleDate);
        }

        LeaveTypeId = leaveTypeId;
        LeaveTypeCodeSnapshot = code;
        StartDate = start;
        EndDate = end;
        StartTime = startTime;
        EndTime = endTime;
        HalfDayPeriod = halfDayPeriod;
        CalculatedDays = decimal.Round(days, 4);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }
}
