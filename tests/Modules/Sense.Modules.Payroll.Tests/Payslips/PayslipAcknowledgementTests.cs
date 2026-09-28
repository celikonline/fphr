using NSubstitute;
using Sense.Modules.Payroll.Application.Payslips;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;
using static Sense.Modules.Payroll.Tests.Payslips.PayslipTestKit;

namespace Sense.Modules.Payroll.Tests.Payslips;

/// <summary>E2/K4: a paid period opens payslips to their employees, who acknowledge once (server time + IP + client).</summary>
public sealed class PayslipAcknowledgementTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();

    [Fact]
    public async Task Acknowledge_sets_timestamp_and_ip_once()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        var handler = new AcknowledgeMyPayslipHandler(
            CurrentEmployee(Me, "203.0.113.7"), PayslipRepository(payslip), PeriodRepository(period), Saver(), ClockAt(Now));

        var first = await handler.Handle(new AcknowledgeMyPayslipCommand(payslip.Id, "Mozilla/5.0 (Test)"), CancellationToken.None);
        var second = await handler.Handle(new AcknowledgeMyPayslipCommand(payslip.Id, "Other/1.0"), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        first.Value.PayslipId.ShouldBe(payslip.Id);
        first.Value.AcknowledgedAt.ShouldBe(Now);
        first.Value.NetPay.ShouldBe(38_000m);
        payslip.AcknowledgedAt.ShouldBe(Now);
        payslip.AcknowledgedFromIp.ShouldBe("203.0.113.7");
        payslip.AcknowledgedUserAgent.ShouldBe("Mozilla/5.0 (Test)");
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(PayrollDomainErrors.PayslipAlreadyAcknowledged);
        payslip.AcknowledgedUserAgent.ShouldBe("Mozilla/5.0 (Test)");
    }

    [Fact]
    public async Task Acknowledge_that_loses_the_save_race_is_a_conflict()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        var lost = Saver(Sense.Shared.Kernel.Results.Error.Conflict(PayrollDomainErrors.PayslipConcurrentUpdate));

        var result = await new AcknowledgeMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(payslip), PeriodRepository(period), lost, ClockAt(Now))
            .Handle(new AcknowledgeMyPayslipCommand(payslip.Id, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipConcurrentUpdate);
        result.Error.Type.ShouldBe(Sense.Shared.Kernel.Results.ErrorType.Conflict);
    }

    [Fact]
    public async Task Acknowledge_before_publish_fails()
    {
        var unpaid = OpenPeriod();
        var draft = PayslipOf(Me, unpaid, published: false);
        var handler = new AcknowledgeMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(draft), PeriodRepository(unpaid), Saver(), ClockAt(Now));

        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(draft.Id, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotPublished);
        result.Error.Type.ShouldBe(Sense.Shared.Kernel.Results.ErrorType.Rule); // 422 (F11 contract)
        draft.AcknowledgedAt.ShouldBeNull();
        draft.Acknowledge(Now, "1.2.3.4", null).Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotPublished);
    }

    [Fact]
    public async Task Acknowledge_is_refused_while_the_period_is_reopened()
    {
        var period = PaidPeriod();
        var payslip = PayslipOf(Me, period);
        period.Reopen("Hatalı prim", Now);
        var handler = new AcknowledgeMyPayslipHandler(CurrentEmployee(Me), PayslipRepository(payslip), PeriodRepository(period), Saver(), ClockAt(Now));

        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(payslip.Id, null), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotPublished);
        payslip.AcknowledgedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Employee_cannot_read_others_payslip()
    {
        var period = PaidPeriod();
        var mine = PayslipOf(Me, period);
        var theirs = PayslipOf(Colleague, period);
        var payslips = PayslipRepository(mine, theirs);
        var periods = PeriodRepository(period);
        var me = CurrentEmployee(Me);
        var documents = Substitute.For<IPayslipDocumentProvider>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var list = await new ListMyPayslipsHandler(me, payslips, periods).Handle(new ListMyPayslipsQuery(), CancellationToken.None);
        var document = await new ExportMyPayslipDocumentHandler(me, payslips, periods, documents, tenant)
            .Handle(new ExportMyPayslipDocumentQuery(theirs.Id), CancellationToken.None);
        var acknowledge = await new AcknowledgeMyPayslipHandler(me, payslips, periods, Saver(), ClockAt(Now))
            .Handle(new AcknowledgeMyPayslipCommand(theirs.Id, null), CancellationToken.None);

        list.Value.Select(p => p.PayslipId).ShouldBe([mine.Id]);
        document.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
        acknowledge.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
        theirs.AcknowledgedAt.ShouldBeNull();
        await documents.DidNotReceiveWithAnyArgs().RenderPdfAsync(default, default, default);
    }

    [Fact]
    public async Task Own_published_payslip_document_is_rendered()
    {
        var period = PaidPeriod();
        var mine = PayslipOf(Me, period);
        var documents = Substitute.For<IPayslipDocumentProvider>();
        var file = new ExportFileResult("p.pdf", "application/pdf", [1, 2, 3]);
        documents.RenderPdfAsync(TenantId, mine.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExportFileResult?>(file));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var result = await new ExportMyPayslipDocumentHandler(CurrentEmployee(Me), PayslipRepository(mine), PeriodRepository(period), documents, tenant)
            .Handle(new ExportMyPayslipDocumentQuery(mine.Id), CancellationToken.None);

        result.Value.ShouldBe(file);
    }

    [Fact]
    public async Task My_list_shows_only_published_payslips_of_paid_periods_newest_first()
    {
        var july = PaidPeriod(7);
        var august = PaidPeriod(8);
        var september = OpenPeriod(9);
        var julySlip = PayslipOf(Me, july);
        var augustSlip = PayslipOf(Me, august);
        var draft = PayslipOf(Me, september, published: false);
        augustSlip.Acknowledge(Now, "1.2.3.4", null);

        var result = await new ListMyPayslipsHandler(CurrentEmployee(Me), PayslipRepository(julySlip, augustSlip, draft), PeriodRepository(july, august, september))
            .Handle(new ListMyPayslipsQuery(), CancellationToken.None);

        result.Value.Select(p => p.Month).ShouldBe([8, 7]);
        result.Value[0].AcknowledgedAt.ShouldBe(Now);
        result.Value[0].NetPay.ShouldBe(38_000m);
        result.Value[0].GrossPay.ShouldBe(50_000m);
        result.Value[0].Currency.ShouldBe("TRY");
        result.Value[1].AcknowledgedAt.ShouldBeNull();
        result.Value[1].ObjectionStatus.ShouldBeNull();
    }

    [Fact]
    public async Task User_without_employee_record_gets_an_error()
    {
        var result = await new ListMyPayslipsHandler(CurrentEmployee(null), PayslipRepository(), PeriodRepository())
            .Handle(new ListMyPayslipsQuery(), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.NoEmployeeForUser);
    }

    [Fact]
    public async Task MarkPaid_publishes_every_payslip_of_the_period()
    {
        var period = OpenPeriod();
        period.Approve(null);
        var first = PayslipOf(Me, period, published: false);
        var second = PayslipOf(Colleague, period, published: false);
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns([first, second]);

        var result = await new MarkPayrollPeriodPaidHandler(PeriodRepository(period), payslips, Substitute.For<IIntegrationEventOutbox>(), ClockAt(PaidAt))
            .Handle(new MarkPayrollPeriodPaidCommand(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        first.PublishedAt.ShouldBe(PaidAt);
        second.PublishedAt.ShouldBe(PaidAt);
    }

    [Fact]
    public void Recalculation_requires_a_new_publication_and_acknowledgement()
    {
        var payslip = PayslipOf(Me, PaidPeriod());
        payslip.Acknowledge(Now, "1.2.3.4", "UA");

        payslip.Recalculate("{}", "hash2", 51_000m, 39_000m, 61_000m);

        payslip.PublishedAt.ShouldBeNull();
        payslip.AcknowledgedAt.ShouldBeNull();
        payslip.AcknowledgedFromIp.ShouldBeNull();
        payslip.NetAmount.ShouldBe(39_000m);
    }

    [Fact]
    public void Acknowledgement_keeps_evidence_within_column_limits()
    {
        var payslip = PayslipOf(Me, PaidPeriod());

        payslip.Acknowledge(Now, "  10.0.0.1 ", new string('x', PayrollLimits.UserAgentMaxLength + 50)).IsSuccess.ShouldBeTrue();

        payslip.AcknowledgedFromIp.ShouldBe("10.0.0.1");
        payslip.AcknowledgedUserAgent!.Length.ShouldBe(PayrollLimits.UserAgentMaxLength);
    }
}
