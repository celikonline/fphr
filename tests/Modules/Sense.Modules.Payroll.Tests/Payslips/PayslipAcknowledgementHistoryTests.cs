using Sense.Modules.Payroll.Domain;
using Shouldly;
using Xunit;
using static Sense.Modules.Payroll.Tests.Payslips.PayslipTestKit;

namespace Sense.Modules.Payroll.Tests.Payslips;

/// <summary>K4 evidence survives a recalculation: the acknowledgement/objection of an earlier version moves to an append-only history.</summary>
public sealed class PayslipAcknowledgementHistoryTests
{
    private static readonly Guid Me = Guid.NewGuid();

    [Fact]
    public void Recalculate_preserves_previous_acknowledgement_in_history()
    {
        var payslip = PayslipOf(Me, PaidPeriod());
        payslip.Acknowledge(Now, "203.0.113.7", "Mozilla/5.0 (Test)");

        payslip.Recalculate("{}", "hash2", 51_000m, 39_000m, 61_000m);
        var objectedAt = Now.AddDays(1);
        var decidedAt = Now.AddDays(2);
        var approvalId = Guid.NewGuid();
        payslip.Publish(Now.AddHours(1));
        payslip.FileObjection("Prim eksik", objectedAt);
        payslip.AttachObjectionApproval(approvalId);
        payslip.RejectObjection(approvalId, decidedAt);
        payslip.Recalculate("{}", "hash3", 52_000m, 40_000m, 62_000m);

        payslip.AcknowledgementHistory.Count.ShouldBe(2);
        var first = payslip.AcknowledgementHistory[0];
        first.Version.ShouldBe(1);
        first.AcknowledgedAt.ShouldBe(Now);
        first.Ip.ShouldBe("203.0.113.7");
        first.UserAgent.ShouldBe("Mozilla/5.0 (Test)");
        first.ObjectionStatus.ShouldBeNull();
        var second = payslip.AcknowledgementHistory[1];
        second.Version.ShouldBe(2);
        second.AcknowledgedAt.ShouldBeNull();
        second.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Rejected);
        second.ObjectionReason.ShouldBe("Prim eksik");
        second.ObjectedAt.ShouldBe(objectedAt);
        second.ObjectionResolvedAt.ShouldBe(decidedAt);

        payslip.Version.ShouldBe(3);
        payslip.PublishedAt.ShouldBeNull();
        payslip.AcknowledgedAt.ShouldBeNull();
        payslip.AcknowledgedFromIp.ShouldBeNull();
        payslip.AcknowledgedUserAgent.ShouldBeNull();
        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.None);
        payslip.ObjectionReason.ShouldBeNull();
        payslip.NetAmount.ShouldBe(40_000m);
    }

    [Fact]
    public void Recalculate_without_acknowledgement_adds_no_history()
    {
        var published = PayslipOf(Me, PaidPeriod());
        var draft = PayslipOf(Me, OpenPeriod(), published: false);

        published.Recalculate("{}", "hash2", 51_000m, 39_000m, 61_000m);
        draft.Recalculate("{}", "hash2", 51_000m, 39_000m, 61_000m);

        published.AcknowledgementHistory.ShouldBeEmpty();
        draft.AcknowledgementHistory.ShouldBeEmpty();
    }

    [Fact]
    public void Recalculate_keeps_a_pending_objection_running_and_out_of_history()
    {
        var payslip = PayslipOf(Me, PaidPeriod());
        payslip.FileObjection("Prim eksik", Now);
        payslip.AttachObjectionApproval(Guid.NewGuid());

        payslip.Recalculate("{}", "hash2", 51_000m, 39_000m, 61_000m);

        payslip.AcknowledgementHistory.ShouldBeEmpty();
        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Pending);
    }
}
