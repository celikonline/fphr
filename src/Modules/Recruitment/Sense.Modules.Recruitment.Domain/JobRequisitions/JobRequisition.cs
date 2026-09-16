using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.JobRequisitions;

/// <summary>Why headcount is being requested.</summary>
public enum RequisitionReason
{
    NewPosition,
    Replacement,
}

/// <summary>
/// A requisition's lifecycle (roadmap #87 flow #2 "requisition submit→approval→posting unlock").
/// <see cref="Draft"/> is editable; <see cref="PendingApproval"/> is snapshotted and awaiting Workflow's decision;
/// <see cref="Approved"/>/<see cref="Rejected"/>/<see cref="Cancelled"/> are terminal.
/// </summary>
public enum RequisitionStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// A headcount request (Kolay İK "Talep formu") that gates publishing a <see cref="JobPostings.JobPosting"/> (R1
/// scope: submit → Workflow approval → the linked posting may publish). Requisition/approval-template field
/// customization and department-specific templates (roadmap #87's full form) are deferred past R1.
/// </summary>
public sealed class JobRequisition : TenantAggregateRoot<Guid>, IAuditLogged
{
    private JobRequisition()
    {
    }

    private JobRequisition(
        Guid id, Guid tenantId, string title, Guid orgUnitId, RecruitmentEmploymentType employmentType,
        int headcountRequested, RequisitionReason reason, string? justification, Guid requesterEmployeeId)
        : base(id, tenantId)
    {
        Title = title;
        OrgUnitId = orgUnitId;
        EmploymentType = employmentType;
        HeadcountRequested = headcountRequested;
        Reason = reason;
        Justification = justification;
        RequesterEmployeeId = requesterEmployeeId;
        Status = RequisitionStatus.Draft;
    }

    public string Title { get; private set; } = string.Empty;

    public Guid OrgUnitId { get; private set; }

    public RecruitmentEmploymentType EmploymentType { get; private set; }

    public int HeadcountRequested { get; private set; }

    public RequisitionReason Reason { get; private set; }

    public string? Justification { get; private set; }

    public Guid RequesterEmployeeId { get; private set; }

    public RequisitionStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public static Result<JobRequisition> Create(
        Guid tenantId, string title, Guid orgUnitId, RecruitmentEmploymentType employmentType,
        int headcountRequested, RequisitionReason reason, string? justification, Guid requesterEmployeeId, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(RecruitmentDomainErrors.RequisitionTitleRequired);
        }

        if (headcountRequested <= 0)
        {
            return Error.Validation(RecruitmentDomainErrors.RequisitionHeadcountInvalid);
        }

        return new JobRequisition(
            id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(title.Trim(), RecruitmentLimits.TitleMaxLength),
            orgUnitId, employmentType, headcountRequested, reason, justification, requesterEmployeeId);
    }

    /// <summary>Draft -> PendingApproval; the caller starts the Workflow approval and calls <see cref="AttachApproval"/> with its id.</summary>
    public Result Submit()
    {
        if (Status != RequisitionStatus.Draft)
        {
            return Error.Rule(RecruitmentDomainErrors.RequisitionNotSubmittable);
        }

        Status = RequisitionStatus.PendingApproval;
        return Result.Success();
    }

    public void AttachApproval(Guid approvalRequestId) => ApprovalRequestId = approvalRequestId;

    /// <summary>Workflow approved the linked approval request.</summary>
    public Result Approve(Guid approvalRequestId, DateTime decidedAt)
    {
        if (Status != RequisitionStatus.PendingApproval || ApprovalRequestId != approvalRequestId)
        {
            return Error.Rule(RecruitmentDomainErrors.RequisitionNotPending);
        }

        Status = RequisitionStatus.Approved;
        DecidedAt = decidedAt;
        return Result.Success();
    }

    /// <summary>Workflow rejected the linked approval request.</summary>
    public Result Reject(Guid approvalRequestId, DateTime decidedAt)
    {
        if (Status != RequisitionStatus.PendingApproval || ApprovalRequestId != approvalRequestId)
        {
            return Error.Rule(RecruitmentDomainErrors.RequisitionNotPending);
        }

        Status = RequisitionStatus.Rejected;
        DecidedAt = decidedAt;
        return Result.Success();
    }

    /// <summary>Draft/PendingApproval -> Cancelled; the caller cancels the Workflow approval first if one is pending.</summary>
    public Result Cancel()
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.PendingApproval))
        {
            return Error.Rule(RecruitmentDomainErrors.RequisitionNotCancellable);
        }

        Status = RequisitionStatus.Cancelled;
        return Result.Success();
    }
}
