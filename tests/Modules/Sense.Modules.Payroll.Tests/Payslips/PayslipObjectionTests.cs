using NSubstitute;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Payslips;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;
using static Sense.Modules.Payroll.Tests.Payslips.PayslipTestKit;

namespace Sense.Modules.Payroll.Tests.Payslips;

/// <summary>E2 / spec §5.5: an objection starts a Workflow "PayslipObjection" approval; approval = objection accepted.</summary>
public sealed class PayslipObjectionTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid ApprovalRequestId = Guid.NewGuid();

    private static IWorkflowService Workflow(Result<Guid> started)
    {
        var workflow = Substitute.For<IWorkflowService>();
        workflow.StartAsync(default!, default, default, default, default, default, default).ReturnsForAnyArgs(Task.FromResult(started));
        return workflow;
    }

    [Fact]
    public async Task Object_starts_PayslipObjection_workflow()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        var workflow = Workflow(Result.Success(ApprovalRequestId));
        var handler = new ObjectToMyPayslipHandler(CurrentEmployee(Me, "198.51.100.4"), PayslipRepository(payslip), PeriodRepository(period), workflow, Saver(), ClockAt(Now));

        var result = await handler.Handle(new ObjectToMyPayslipCommand(payslip.Id, "  Fazla mesai eksik  ", "UA/1"), CancellationToken.None);

        result.Value.PayslipId.ShouldBe(payslip.Id);
        result.Value.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Pending);
        result.Value.ObjectionReason.ShouldBe("Fazla mesai eksik");
        await workflow.Received(1).StartAsync(
            ApprovalRequestTypes.PayslipObjection, payslip.Id, Me, null, "198.51.100.4", "UA/1", Arg.Any<CancellationToken>());
        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Pending);
        payslip.ObjectionReason.ShouldBe("Fazla mesai eksik");
        payslip.ObjectedAt.ShouldBe(Now);
        payslip.ObjectionApprovalRequestId.ShouldBe(ApprovalRequestId);
    }

    [Fact]
    public async Task Object_on_others_payslip_is_not_found_and_starts_nothing()
    {
        var period = PaidPeriod();
        var theirs = PayslipOf(Guid.NewGuid(), period);
        var workflow = Workflow(Result.Success(ApprovalRequestId));

        var result = await new ObjectToMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(theirs), PeriodRepository(period), workflow, Saver(), ClockAt(Now))
            .Handle(new ObjectToMyPayslipCommand(theirs.Id, "Hatalı", null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
        theirs.ObjectionStatus.ShouldBe(PayslipObjectionStatus.None);
        await workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Object_that_loses_the_save_race_starts_no_workflow()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        var workflow = Workflow(Result.Success(ApprovalRequestId));
        var lost = Saver(Error.Conflict(PayrollDomainErrors.PayslipConcurrentUpdate));

        var result = await new ObjectToMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(payslip), PeriodRepository(period), workflow, lost, ClockAt(Now))
            .Handle(new ObjectToMyPayslipCommand(payslip.Id, "Hatalı", null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipConcurrentUpdate);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Object_after_acknowledgement_is_refused()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        payslip.Acknowledge(Now, "1.2.3.4", null);
        var workflow = Workflow(Result.Success(ApprovalRequestId));

        var result = await new ObjectToMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(payslip), PeriodRepository(period), workflow, Saver(), ClockAt(Now))
            .Handle(new ObjectToMyPayslipCommand(payslip.Id, "Hatalı", null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipAlreadyAcknowledged);
        await workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Object_when_workflow_fails_leaves_the_payslip_untouched()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        var workflow = Workflow(Result.Failure<Guid>(Error.Validation("workflow.errors.no_approver")));

        var result = await new ObjectToMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(payslip), PeriodRepository(period), workflow, Saver(), ClockAt(Now))
            .Handle(new ObjectToMyPayslipCommand(payslip.Id, "Hatalı", null), CancellationToken.None);

        result.Error.Code.ShouldBe("workflow.errors.no_approver");
        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.None);
        payslip.ObjectionReason.ShouldBeNull();
    }

    [Fact]
    public void Objection_needs_a_reason_and_blocks_acknowledgement_while_pending()
    {
        var payslip = PayslipOf(Me, PaidPeriod());

        payslip.FileObjection("   ", Now).Error.Code.ShouldBe(PayrollDomainErrors.PayslipObjectionReasonInvalid);
        payslip.FileObjection(new string('x', PayrollLimits.ObjectionReasonMaxLength + 1), Now).Error.Code
            .ShouldBe(PayrollDomainErrors.PayslipObjectionReasonInvalid);
        payslip.FileObjection("Hatalı", Now).IsSuccess.ShouldBeTrue();

        payslip.FileObjection("Tekrar", Now).Error.Code.ShouldBe(PayrollDomainErrors.PayslipObjectionAlreadyFiled);
        var pending = payslip.Acknowledge(Now, "1.2.3.4", null);
        pending.Error.Code.ShouldBe(PayrollDomainErrors.PayslipObjectionPending);
        pending.Error.Type.ShouldBe(ErrorType.Conflict); // 409 (F11 contract)
    }

    [Fact]
    public async Task Objection_accepted_marks_status_and_notifies_payroll_role()
    {
        var period = PaidPeriod();
        var payslip = PendingObjection(period);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();
        var handler = new PayslipObjectionApprovalCompletedHandler(PayslipRepository(payslip), PeriodRepository(period), outbox, unitOfWork, ClockAt(Now));

        await handler.Handle(
            new ApprovalCompleted(TenantId, ApprovalRequestId, ApprovalRequestTypes.PayslipObjection, payslip.Id, Me), CancellationToken.None);

        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Accepted);
        payslip.ObjectionDecidedAt.ShouldBe(Now);
        outbox.Received(1).Enqueue(Arg.Is<PayslipObjectionAccepted>(e =>
            e.PayslipId == payslip.Id && e.EmployeeId == Me && e.PeriodId == period.Id && e.Year == 2026 && e.Month == 8
            && e.ApprovalRequestId == ApprovalRequestId && e.NotifyRoleCode == PayrollRoleCodes.PayrollSpecialist));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Objection_accepted_ignores_other_request_types_and_stale_requests()
    {
        var period = PaidPeriod();
        var payslip = PendingObjection(period);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new PayslipObjectionApprovalCompletedHandler(
            PayslipRepository(payslip), PeriodRepository(period), outbox, Substitute.For<IPayrollUnitOfWork>(), ClockAt(Now));

        await handler.Handle(new ApprovalCompleted(TenantId, ApprovalRequestId, ApprovalRequestTypes.Overtime, payslip.Id, Me), CancellationToken.None);
        await handler.Handle(new ApprovalCompleted(TenantId, Guid.NewGuid(), ApprovalRequestTypes.PayslipObjection, payslip.Id, Me), CancellationToken.None);

        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Pending);
        outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task Objection_rejected_marks_status_and_allows_acknowledgement()
    {
        var period = PaidPeriod();
        var payslip = PendingObjection(period);
        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();

        await new PayslipObjectionApprovalRejectedHandler(PayslipRepository(payslip), unitOfWork, ClockAt(Now))
            .Handle(new ApprovalRejected(TenantId, ApprovalRequestId, ApprovalRequestTypes.PayslipObjection, payslip.Id, Me, 1), CancellationToken.None);

        payslip.ObjectionStatus.ShouldBe(PayslipObjectionStatus.Rejected);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        payslip.Acknowledge(Now, "1.2.3.4", null).IsSuccess.ShouldBeTrue();
    }

    private static Domain.Payslips.Payslip PendingObjection(Domain.Periods.PayrollPeriod period)
    {
        var payslip = PayslipOf(Me, period);
        payslip.FileObjection("Prim eksik", Now).IsSuccess.ShouldBeTrue();
        payslip.AttachObjectionApproval(ApprovalRequestId);
        return payslip;
    }
}
