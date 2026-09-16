using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class JobRequisitionTests
{
    private static JobRequisition NewDraft() =>
        JobRequisition.Create(
            Guid.NewGuid(), "Senior Backend Developer", Guid.NewGuid(), RecruitmentEmploymentType.FullTime,
            2, RequisitionReason.NewPosition, "Team growth", Guid.NewGuid()).Value;

    [Fact]
    public void Create_ZeroHeadcount_IsRejected()
    {
        var result = JobRequisition.Create(
            Guid.NewGuid(), "Title", Guid.NewGuid(), RecruitmentEmploymentType.FullTime, 0, RequisitionReason.NewPosition, null, Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionHeadcountInvalid);
    }

    [Fact]
    public void Submit_FromDraft_MovesToPendingApproval()
    {
        var requisition = NewDraft();

        requisition.Submit().IsSuccess.ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.PendingApproval);
    }

    [Fact]
    public void Submit_Twice_IsRejected()
    {
        var requisition = NewDraft();
        requisition.Submit();

        var result = requisition.Submit();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionNotSubmittable);
    }

    [Fact]
    public void Approve_WrongApprovalRequestId_IsRejected()
    {
        var requisition = NewDraft();
        requisition.Submit();
        requisition.AttachApproval(Guid.NewGuid());

        var result = requisition.Approve(Guid.NewGuid(), DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionNotPending);
    }

    [Fact]
    public void Approve_MatchingApprovalRequestId_Succeeds()
    {
        var requisition = NewDraft();
        requisition.Submit();
        var approvalId = Guid.NewGuid();
        requisition.AttachApproval(approvalId);

        var result = requisition.Approve(approvalId, DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        requisition.Status.ShouldBe(RequisitionStatus.Approved);
        requisition.DecidedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Cancel_AfterApproval_IsRejected()
    {
        var requisition = NewDraft();
        requisition.Submit();
        var approvalId = Guid.NewGuid();
        requisition.AttachApproval(approvalId);
        requisition.Approve(approvalId, DateTime.UtcNow);

        var result = requisition.Cancel();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionNotCancellable);
    }

    [Fact]
    public void Cancel_FromDraft_Succeeds()
    {
        var requisition = NewDraft();

        requisition.Cancel().IsSuccess.ShouldBeTrue();

        requisition.Status.ShouldBe(RequisitionStatus.Cancelled);
    }
}
