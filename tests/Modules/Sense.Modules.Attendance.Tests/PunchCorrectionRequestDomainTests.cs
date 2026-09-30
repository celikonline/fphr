using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Corrections;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>FG-004 PunchCorrectionRequest invariants and status transitions.</summary>
public sealed class PunchCorrectionRequestDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 21);
    private static readonly DateTime Now = new(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);

    private static PunchCorrectionRequest Pending(Guid approvalId)
    {
        var request = PunchCorrectionRequest.Submit(TenantId, EmployeeId, Day, null, new TimeOnly(18, 0), "Kart okutmayı unuttum", new TimeOnly(9, 0), null, Now).Value;
        request.AttachApproval(approvalId);
        return request;
    }

    [Fact]
    public void Submit_WithOnlyCheckOut_IsPending_AndKeepsTheDaySnapshot()
    {
        var result = PunchCorrectionRequest.Submit(TenantId, EmployeeId, Day, null, new TimeOnly(18, 0), "  Kart okutmayı unuttum  ", new TimeOnly(9, 0), null, Now);

        result.IsSuccess.ShouldBeTrue();
        var request = result.Value;
        request.Status.ShouldBe(PunchCorrectionStatus.Pending);
        request.AttendanceDate.ShouldBe(Day);
        request.RequestedCheckIn.ShouldBeNull();
        request.RequestedCheckOut.ShouldBe(new TimeOnly(18, 0));
        request.CurrentCheckIn.ShouldBe(new TimeOnly(9, 0));
        request.CurrentCheckOut.ShouldBeNull();
        request.Reason.ShouldBe("Kart okutmayı unuttum");
        request.SubmittedAt.ShouldBe(Now);
        request.DecidedAt.ShouldBeNull();
    }

    [Fact]
    public void Submit_WithoutAnyTime_IsRejected()
    {
        var result = PunchCorrectionRequest.Submit(TenantId, EmployeeId, Day, null, null, "gerekçe", null, null, Now);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionTimesRequired);
    }

    [Theory]
    [InlineData(18, 0, 9, 0)]
    [InlineData(9, 0, 9, 0)]
    public void Submit_CheckOutNotAfterCheckIn_IsRejected(int inHour, int inMinute, int outHour, int outMinute)
    {
        var result = PunchCorrectionRequest.Submit(
            TenantId, EmployeeId, Day, new TimeOnly(inHour, inMinute), new TimeOnly(outHour, outMinute), "gerekçe", null, null, Now);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionInvalidRange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_WithoutReason_IsRejected(string? reason)
    {
        var result = PunchCorrectionRequest.Submit(TenantId, EmployeeId, Day, new TimeOnly(9, 0), null, reason, null, null, Now);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionReasonRequired);
    }

    [Fact]
    public void Approve_ForTheAttachedApproval_ClosesAsApproved()
    {
        var approvalId = Guid.NewGuid();
        var request = Pending(approvalId);

        request.Approve(approvalId, Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(PunchCorrectionStatus.Approved);
        request.DecidedAt.ShouldBe(Now);
        request.IsAwaiting(approvalId).ShouldBeFalse();
    }

    [Fact]
    public void Decision_ForAnotherApproval_IsIgnored()
    {
        var request = Pending(Guid.NewGuid());

        request.Approve(Guid.NewGuid(), Now).Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionNotPending);
        request.Reject(Guid.NewGuid(), Now).Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionNotPending);
        request.Status.ShouldBe(PunchCorrectionStatus.Pending);
    }

    [Fact]
    public void MarkApplyFailed_And_Reject_CloseThePendingRequest()
    {
        var approvalId = Guid.NewGuid();
        var failed = Pending(approvalId);
        var rejected = Pending(approvalId);

        failed.MarkApplyFailed(approvalId, Now).IsSuccess.ShouldBeTrue();
        rejected.Reject(approvalId, Now).IsSuccess.ShouldBeTrue();

        failed.Status.ShouldBe(PunchCorrectionStatus.ApplyFailed);
        rejected.Status.ShouldBe(PunchCorrectionStatus.Rejected);
        rejected.Approve(approvalId, Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Withdraw_OnlyWhilePending()
    {
        var approvalId = Guid.NewGuid();
        var request = Pending(approvalId);

        request.Withdraw(Now).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(PunchCorrectionStatus.Withdrawn);
        request.DecidedAt.ShouldBe(Now);

        request.Withdraw(Now).Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionNotPending);
        request.Approve(approvalId, Now).IsFailure.ShouldBeTrue();
    }
}
