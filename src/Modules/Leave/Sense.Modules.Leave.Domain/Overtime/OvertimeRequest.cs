using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Overtime;

public enum OvertimeRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Withdrawn = 3,
    Cancelled = 4,
}

/// <summary>11.3.5 "OvertimeRequest.Source": Manual = self-service, Admin = <c>CreateOvertimeRequestOnBehalf</c>.</summary>
public enum OvertimeRequestSource
{
    Manual = 0,
    Attendance = 1,
    Admin = 2,
    Import = 3,
}

/// <summary>Day kind of the overtime start date (computed; a multiplier hint for conversions).</summary>
public enum OvertimeDayKind
{
    Workday = 0,
    WeeklyRest = 1,
    PublicHoliday = 2,
}

/// <summary>
/// Overtime ("fazla mesai") request. Times are the employee's local wall-clock: <see cref="WorkDate"/> + <see cref="StartTime"/>
/// for <see cref="DurationMinutes"/> (may cross midnight). <see cref="PayableMinutes"/> = DurationMinutes − the part that
/// overlaps the planned work schedule (11.3.5 15.09 addendum). Lifecycle mirrors LeaveRequest: Pending → Approved | Rejected
/// (Workflow "Overtime" approval) | Withdrawn (requester); an approved request can be converted once (<see cref="ConversionId"/>).
/// </summary>
public sealed class OvertimeRequest : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OvertimeRequest()
    {
    }

    private OvertimeRequest(Guid tenantId, Guid employeeId, OvertimeRequestSource source, DateTime submittedAt, Guid? submittedByUserId, string? ip)
        : base(Guid.CreateVersion7(), tenantId)
    {
        EmployeeId = employeeId;
        Source = source;
        Status = OvertimeRequestStatus.Pending;
        SubmittedAt = submittedAt;
        SubmittedByUserId = submittedByUserId;
        SubmittedIpAddress = ip;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly WorkDate { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public int DurationMinutes { get; private set; }

    public OvertimeDayKind DayKind { get; private set; }

    public int OverlapWithScheduleMinutes { get; private set; }

    public int PayableMinutes { get; private set; }

    public string? Description { get; private set; }

    public OvertimeRequestSource Source { get; private set; }

    public OvertimeRequestStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public string? SubmittedIpAddress { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public Guid? ConversionId { get; private set; }

    public DateTime StartAt => WorkDate.ToDateTime(StartTime);

    public DateTime EndAt => StartAt.AddMinutes(DurationMinutes);

    public bool IsActive => Status is OvertimeRequestStatus.Pending or OvertimeRequestStatus.Approved;

    public static Result<OvertimeRequest> Submit(
        Guid tenantId, Guid employeeId, DateOnly workDate, TimeOnly startTime, int durationMinutes, OvertimeDayKind dayKind, int overlapWithScheduleMinutes,
        string? description, OvertimeRequestSource source, DateTime submittedAt, Guid? submittedByUserId, string? ip)
    {
        if (durationMinutes <= 0 || durationMinutes > OvertimeLimits.MaxDurationMinutes)
        {
            return Error.Validation(LeaveDomainErrors.OvertimeInvalidDuration, ("max", OvertimeLimits.MaxDurationMinutes));
        }

        if (overlapWithScheduleMinutes < 0 || overlapWithScheduleMinutes > durationMinutes)
        {
            return Error.Validation(LeaveDomainErrors.OvertimeInvalidOverlap);
        }

        return new OvertimeRequest(tenantId, employeeId, source, submittedAt, submittedByUserId, ip)
        {
            WorkDate = workDate,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            DayKind = dayKind,
            OverlapWithScheduleMinutes = overlapWithScheduleMinutes,
            PayableMinutes = durationMinutes - overlapWithScheduleMinutes,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    public Result Approve(Guid approvalRequestId, DateTime at) => Decide(approvalRequestId, OvertimeRequestStatus.Approved, at);

    public Result Reject(Guid approvalRequestId, DateTime at) => Decide(approvalRequestId, OvertimeRequestStatus.Rejected, at);

    public Result Withdraw(DateTime at)
    {
        if (Status != OvertimeRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeNotPending);
        }

        Status = OvertimeRequestStatus.Withdrawn;
        DecidedAt = at;
        return Result.Success();
    }

    /// <summary>Records the (single) posted conversion; only approved, not yet converted requests.</summary>
    public Result MarkConverted(Guid conversionId)
    {
        if (Status != OvertimeRequestStatus.Approved)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeNotApproved);
        }

        if (ConversionId is not null)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeAlreadyConverted);
        }

        ConversionId = conversionId;
        return Result.Success();
    }

    public bool Overlaps(DateTime start, DateTime end) => StartAt < end && start < EndAt;

    private Result Decide(Guid approvalRequestId, OvertimeRequestStatus status, DateTime at)
    {
        if (Status != OvertimeRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.OvertimeNotPending);
        }

        if (ApprovalRequestId != approvalRequestId)
        {
            return Error.Conflict(LeaveDomainErrors.RequestApprovalMismatch);
        }

        Status = status;
        DecidedAt = at;
        return Result.Success();
    }
}

public static class OvertimeLimits
{
    /// <summary>A single record is at most one day long (4857 md.63/69: daily work incl. overtime is capped at 11 hours anyway).</summary>
    public const int MaxDurationMinutes = 24 * 60;

    public const decimal MinRate = 1m;

    public const decimal MaxRate = 3m;
}
