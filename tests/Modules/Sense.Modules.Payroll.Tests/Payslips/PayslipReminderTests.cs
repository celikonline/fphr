using NSubstitute;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Payslips;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Shouldly;
using Xunit;
using static Sense.Modules.Payroll.Tests.Payslips.PayslipTestKit;

namespace Sense.Modules.Payroll.Tests.Payslips;

/// <summary>E2: the daily job (and the manual "Hatırlat" trigger) reminds employees whose published payslip is still unacknowledged.</summary>
public sealed class PayslipReminderTests
{
    [Fact]
    public async Task Reminder_targets_only_unacknowledged_published()
    {
        var paid = PaidPeriod(8);
        var reopened = PaidPeriod(7);
        reopened.Reopen("Düzeltme", Now);
        var waiting = PayslipOf(Guid.NewGuid(), paid);
        var acknowledged = PayslipOf(Guid.NewGuid(), paid);
        acknowledged.Acknowledge(Now, "1.2.3.4", null);
        var draft = PayslipOf(Guid.NewGuid(), paid, published: false);
        var objecting = PayslipOf(Guid.NewGuid(), paid);
        objecting.FileObjection("Eksik prim", Now);
        var rejectedObjection = PayslipOf(Guid.NewGuid(), paid);
        rejectedObjection.FileObjection("Eksik prim", Now);
        var approvalId = Guid.NewGuid();
        rejectedObjection.AttachObjectionApproval(approvalId);
        rejectedObjection.RejectObjection(approvalId, Now);
        var underRevision = PayslipOf(Guid.NewGuid(), reopened);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var service = new PayslipAcknowledgementReminderService(
            PayslipRepository(waiting, acknowledged, draft, objecting, rejectedObjection, underRevision), PeriodRepository(paid, reopened), outbox);

        var count = await service.EnqueueRemindersAsync(TenantId, periodId: null, publishedSince: null, CancellationToken.None);

        count.ShouldBe(2);
        outbox.Received(1).Enqueue(Arg.Is<PayslipAcknowledgementReminderDue>(e =>
            e.TenantId == TenantId
            && e.Payslips.Select(p => p.PayslipId).OrderBy(id => id).SequenceEqual(new[] { waiting.Id, rejectedObjection.Id }.OrderBy(id => id))
            && e.Payslips.All(p => p.PeriodId == paid.Id && p.Year == 2026 && p.Month == 8)));
    }

    [Fact]
    public async Task Reminder_with_nothing_pending_enqueues_nothing()
    {
        var paid = PaidPeriod();
        var acknowledged = PayslipOf(Guid.NewGuid(), paid);
        acknowledged.Acknowledge(Now, "1.2.3.4", null);
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var count = await new PayslipAcknowledgementReminderService(PayslipRepository(acknowledged), PeriodRepository(paid), outbox)
            .EnqueueRemindersAsync(TenantId, null, null, CancellationToken.None);

        count.ShouldBe(0);
        outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task Job_handler_reminds_the_tenant_and_saves()
    {
        var paid = PaidPeriod();
        var waiting = PayslipOf(Guid.NewGuid(), paid);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var unitOfWork = Substitute.For<IPayrollUnitOfWork>();
        var handler = new PayslipAcknowledgementReminderJobHandler(
            new PayslipAcknowledgementReminderService(PayslipRepository(waiting), PeriodRepository(paid), outbox), unitOfWork, ClockAt(Now));

        handler.Handles.ShouldBe(JobType.PayslipAcknowledgementReminder);
        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, "{}", Now), CancellationToken.None);
        await handler.ExecuteAsync(new ScheduledJobContext(null, "{}", Now), CancellationToken.None);

        outbox.Received(1).Enqueue(Arg.Any<PayslipAcknowledgementReminderDue>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reminder_job_targets_only_payslips_published_in_the_last_90_days()
    {
        var recentPeriod = PaidPeriod(8);
        var oldPeriod = PaidPeriod(5);
        var recent = PayslipOf(Guid.NewGuid(), recentPeriod, published: false);
        recent.Publish(Now.AddDays(-PayrollLimits.AcknowledgementReminderWindowDays + 1));
        var old = PayslipOf(Guid.NewGuid(), oldPeriod, published: false);
        old.Publish(Now.AddDays(-PayrollLimits.AcknowledgementReminderWindowDays - 1));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new PayslipAcknowledgementReminderJobHandler(
            new PayslipAcknowledgementReminderService(PayslipRepository(recent, old), PeriodRepository(recentPeriod, oldPeriod), outbox),
            Substitute.For<IPayrollUnitOfWork>(), ClockAt(Now));

        await handler.ExecuteAsync(new ScheduledJobContext(TenantId, "{}", Now), CancellationToken.None);

        outbox.Received(1).Enqueue(Arg.Is<PayslipAcknowledgementReminderDue>(e => e.Payslips.Single().PayslipId == recent.Id));
    }

    [Fact]
    public async Task Manual_trigger_reminds_one_paid_period_and_refuses_an_unpaid_one()
    {
        var paid = PaidPeriod(8);
        var open = OpenPeriod(9);
        var waiting = PayslipOf(Guid.NewGuid(), paid);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var periods = PeriodRepository(paid, open);
        var handler = new SendPayslipAcknowledgementRemindersHandler(
            tenant, periods, new PayslipAcknowledgementReminderService(PayslipRepository(waiting), periods, outbox));

        var sent = await handler.Handle(new SendPayslipAcknowledgementRemindersCommand(paid.Id), CancellationToken.None);
        var refused = await handler.Handle(new SendPayslipAcknowledgementRemindersCommand(open.Id), CancellationToken.None);
        var missing = await handler.Handle(new SendPayslipAcknowledgementRemindersCommand(Guid.NewGuid()), CancellationToken.None);

        sent.Value.ShouldBe(1);
        refused.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotPaid);
        missing.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotFound);
        outbox.Received(1).Enqueue(Arg.Is<PayslipAcknowledgementReminderDue>(e => e.Payslips.Single().PayslipId == waiting.Id));
    }
}
