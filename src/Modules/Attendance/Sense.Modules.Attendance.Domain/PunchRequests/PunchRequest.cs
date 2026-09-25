using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Domain.PunchRequests;

public enum PunchRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

/// <summary>
/// Eksik kayıt talebi (zaman-devam Z2.1, spec §4/§5.5): çalışan unuttuğu bir giriş/çıkışı gerekçesiyle ister; Workflow
/// onayı (<c>RequestType = "AttendancePunch"</c>) gelince uygulama katmanı <c>Source = Request</c> bir
/// <see cref="Punches.Punch"/> üretir ve kimliğini <see cref="Approve"/> ile buraya yazar. Yalnız <see cref="PunchRequestStatus.Pending"/>
/// talep karara bağlanabilir ya da iptal edilebilir.
/// </summary>
public sealed class PunchRequest : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const int ReasonMaxLength = 1000;

    private PunchRequest()
    {
    }

    private PunchRequest(Guid id, Guid tenantId, Guid employeeId, DateOnly workDate, DateTime occurredAtUtc, PunchDirection direction, string reason)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        WorkDate = workDate;
        OccurredAtUtc = occurredAtUtc;
        Direction = direction;
        Reason = reason;
        Status = PunchRequestStatus.Pending;
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly WorkDate { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public PunchDirection Direction { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public PunchRequestStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public Guid? CreatedPunchId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    public static Result<PunchRequest> Create(Guid tenantId, Guid employeeId, DateOnly workDate, DateTime occurredAtUtc, PunchDirection direction, string reason)
    {
        if (employeeId == Guid.Empty)
        {
            return Error.Validation(AttendanceDomainErrors.PunchEmployeeRequired);
        }

        if (direction is not (PunchDirection.In or PunchDirection.Out))
        {
            return Error.Validation(AttendanceDomainErrors.PunchRequestDirectionInvalid);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > ReasonMaxLength)
        {
            return Error.Validation(AttendanceDomainErrors.PunchRequestReasonRequired);
        }

        var occurred = occurredAtUtc.Kind == DateTimeKind.Utc ? occurredAtUtc : DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        return new PunchRequest(Guid.CreateVersion7(), tenantId, employeeId, workDate, occurred, direction, reason.Trim());
    }

    /// <summary>Workflow onay talebi başlatıldığında bağlantısı yazılır.</summary>
    public Result AttachApproval(Guid approvalRequestId)
    {
        var guard = EnsurePending();
        if (guard.IsFailure)
        {
            return guard;
        }

        ApprovalRequestId = approvalRequestId;
        return Result.Success();
    }

    public Result Approve(Guid createdPunchId, DateTime nowUtc) => Decide(PunchRequestStatus.Approved, nowUtc, createdPunchId);

    public Result Reject(DateTime nowUtc) => Decide(PunchRequestStatus.Rejected, nowUtc, null);

    public Result Cancel(DateTime nowUtc) => Decide(PunchRequestStatus.Cancelled, nowUtc, null);

    private Result Decide(PunchRequestStatus status, DateTime nowUtc, Guid? createdPunchId)
    {
        var guard = EnsurePending();
        if (guard.IsFailure)
        {
            return guard;
        }

        Status = status;
        CreatedPunchId = createdPunchId;
        DecidedAtUtc = nowUtc;
        return Result.Success();
    }

    private Result EnsurePending() =>
        Status == PunchRequestStatus.Pending ? Result.Success() : Error.Conflict(AttendanceDomainErrors.PunchRequestNotPending);
}
