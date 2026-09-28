using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.Corrections;

public enum PunchCorrectionStatus
{
    Pending = 0,
    Approved = 1,

    /// <summary>Workflow approved it, but the day could not be changed (the month got locked meanwhile, or the times no longer fit the day).</summary>
    ApplyFailed = 2,
    Rejected = 3,
    Withdrawn = 4,
}

public static class PunchCorrectionLimits
{
    public const int ReasonMaxLength = 500;
}

/// <summary>
/// Eksik giriş/çıkış düzeltme talebi (FG-004): the employee asks for a check-in and/or check-out time on one day; the
/// Workflow "PunchCorrection" approval decides it and, once approved, the times are written to that
/// <see cref="AttendanceDays.AttendanceDay"/> through the shared upsert. <see cref="CurrentCheckIn"/>/<see cref="CurrentCheckOut"/>
/// snapshot the day as it was at submit time so the approver (and later history) sees what changes. Overnight corrections
/// are out of scope: when both times are given check-out must be after check-in. At most one Pending request per
/// (employee, day) - enforced by a partial unique index too.
/// </summary>
public sealed class PunchCorrectionRequest : TenantAggregateRoot<Guid>
{
    private PunchCorrectionRequest()
    {
    }

    private PunchCorrectionRequest(
        Guid id, Guid tenantId, Guid employeeId, DateOnly attendanceDate, TimeOnly? requestedCheckIn, TimeOnly? requestedCheckOut,
        TimeOnly? currentCheckIn, TimeOnly? currentCheckOut, string reason, DateTime submittedAt)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        AttendanceDate = attendanceDate;
        RequestedCheckIn = requestedCheckIn;
        RequestedCheckOut = requestedCheckOut;
        CurrentCheckIn = currentCheckIn;
        CurrentCheckOut = currentCheckOut;
        Reason = reason;
        SubmittedAt = submittedAt;
        Status = PunchCorrectionStatus.Pending;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly AttendanceDate { get; private set; }

    public TimeOnly? RequestedCheckIn { get; private set; }

    public TimeOnly? RequestedCheckOut { get; private set; }

    /// <summary>The day's check-in when the request was submitted (null = none recorded).</summary>
    public TimeOnly? CurrentCheckIn { get; private set; }

    /// <summary>The day's check-out when the request was submitted (null = none recorded).</summary>
    public TimeOnly? CurrentCheckOut { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public PunchCorrectionStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    /// <summary>When the request left Pending (approved, apply failed, rejected or withdrawn).</summary>
    public DateTime? DecidedAt { get; private set; }

    public static Result<PunchCorrectionRequest> Submit(
        Guid tenantId, Guid employeeId, DateOnly attendanceDate, TimeOnly? requestedCheckIn, TimeOnly? requestedCheckOut, string? reason,
        TimeOnly? currentCheckIn, TimeOnly? currentCheckOut, DateTime submittedAt)
    {
        if (requestedCheckIn is null && requestedCheckOut is null)
        {
            return Error.Validation(AttendanceDomainErrors.CorrectionTimesRequired);
        }

        if (requestedCheckIn is not null && requestedCheckOut is not null && requestedCheckOut <= requestedCheckIn)
        {
            return Error.Validation(AttendanceDomainErrors.CorrectionInvalidRange);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation(AttendanceDomainErrors.CorrectionReasonRequired);
        }

        return new PunchCorrectionRequest(
            Guid.CreateVersion7(), tenantId, employeeId, attendanceDate, requestedCheckIn, requestedCheckOut, currentCheckIn, currentCheckOut,
            Guard.MaxLength(reason.Trim(), PunchCorrectionLimits.ReasonMaxLength), submittedAt);
    }

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    /// <summary>True while this request is still Pending on <paramref name="approvalRequestId"/> - a Workflow decision for anything else is ignored.</summary>
    public bool IsAwaiting(Guid approvalRequestId) => Status == PunchCorrectionStatus.Pending && ApprovalRequestId == approvalRequestId;

    public Result Approve(Guid approvalRequestId, DateTime whenUtc) => Close(approvalRequestId, PunchCorrectionStatus.Approved, whenUtc);

    public Result MarkApplyFailed(Guid approvalRequestId, DateTime whenUtc) => Close(approvalRequestId, PunchCorrectionStatus.ApplyFailed, whenUtc);

    public Result Reject(Guid approvalRequestId, DateTime whenUtc) => Close(approvalRequestId, PunchCorrectionStatus.Rejected, whenUtc);

    public Result Withdraw(DateTime whenUtc)
    {
        if (Status != PunchCorrectionStatus.Pending)
        {
            return Error.Conflict(AttendanceDomainErrors.CorrectionNotPending);
        }

        Status = PunchCorrectionStatus.Withdrawn;
        DecidedAt = whenUtc;
        return Result.Success();
    }

    private Result Close(Guid approvalRequestId, PunchCorrectionStatus status, DateTime whenUtc)
    {
        if (!IsAwaiting(approvalRequestId))
        {
            return Error.Conflict(AttendanceDomainErrors.CorrectionNotPending);
        }

        Status = status;
        DecidedAt = whenUtc;
        return Result.Success();
    }
}
