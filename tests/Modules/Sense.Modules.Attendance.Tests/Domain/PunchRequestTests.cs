using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Domain;

/// <summary>Eksik kayıt talebi (Z2.1): yalnız bekleyen talep karara bağlanır.</summary>
public sealed class PunchRequestTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Occurred = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

    private static PunchRequest NewRequest() =>
        PunchRequest.Create(Tenant, Guid.NewGuid(), new DateOnly(2026, 9, 1), Occurred, PunchDirection.In, " kartı unuttum ").Value;

    [Fact]
    public void Create_starts_pending_with_trimmed_reason()
    {
        var request = NewRequest();

        request.Status.ShouldBe(PunchRequestStatus.Pending);
        request.Reason.ShouldBe("kartı unuttum");
        request.ApprovalRequestId.ShouldBeNull();
        request.CreatedPunchId.ShouldBeNull();
    }

    [Fact]
    public void Create_requires_reason_and_known_direction()
    {
        PunchRequest.Create(Tenant, Guid.NewGuid(), new DateOnly(2026, 9, 1), Occurred, PunchDirection.In, "  ").Error.Code
            .ShouldBe(AttendanceDomainErrors.PunchRequestReasonRequired);
        PunchRequest.Create(Tenant, Guid.NewGuid(), new DateOnly(2026, 9, 1), Occurred, PunchDirection.Unknown, "unuttum").Error.Code
            .ShouldBe(AttendanceDomainErrors.PunchRequestDirectionInvalid);
        PunchRequest.Create(Tenant, Guid.Empty, new DateOnly(2026, 9, 1), Occurred, PunchDirection.Out, "unuttum").Error.Code
            .ShouldBe(AttendanceDomainErrors.PunchEmployeeRequired);
    }

    [Fact]
    public void Approve_links_created_punch_and_blocks_further_decisions()
    {
        var request = NewRequest();
        var approvalId = Guid.NewGuid();
        var punchId = Guid.NewGuid();
        request.AttachApproval(approvalId).IsSuccess.ShouldBeTrue();

        request.Approve(punchId, Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(PunchRequestStatus.Approved);
        request.ApprovalRequestId.ShouldBe(approvalId);
        request.CreatedPunchId.ShouldBe(punchId);
        request.DecidedAtUtc.ShouldBe(Now);
        request.Reject(Now).Error.Code.ShouldBe(AttendanceDomainErrors.PunchRequestNotPending);
        request.Cancel(Now).Error.Code.ShouldBe(AttendanceDomainErrors.PunchRequestNotPending);
    }

    [Theory]
    [InlineData(PunchRequestStatus.Rejected)]
    [InlineData(PunchRequestStatus.Cancelled)]
    public void Reject_or_cancel_closes_request_without_punch(PunchRequestStatus target)
    {
        var request = NewRequest();

        var result = target == PunchRequestStatus.Rejected ? request.Reject(Now) : request.Cancel(Now);

        result.IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(target);
        request.CreatedPunchId.ShouldBeNull();
        request.Approve(Guid.NewGuid(), Now).Error.Code.ShouldBe(AttendanceDomainErrors.PunchRequestNotPending);
    }
}
