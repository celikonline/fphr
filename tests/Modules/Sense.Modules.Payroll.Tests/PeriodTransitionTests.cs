using NSubstitute;
using Sense.Modules.Payroll.Application.Closing;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Open → Approved → Paid → Locked over the API, plus the audited "Yeniden aç" that sends a period back to Open.</summary>
public sealed class PeriodTransitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static PayrollPeriod PeriodIn(PayrollPeriodStatus status)
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        if (status >= PayrollPeriodStatus.Approved) period.Approve(null);
        if (status >= PayrollPeriodStatus.Paid) period.MarkPaid();
        if (status >= PayrollPeriodStatus.Locked) period.Lock();
        return period;
    }

    private static IPayrollPeriodRepository Repository(PayrollPeriod period)
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        return periods;
    }

    private static IPayslipDirtyFlagRepository DirtyFlags(int active)
    {
        var flags = Substitute.For<IPayslipDirtyFlagRepository>();
        IReadOnlyList<PayslipDirtyFlag> list = Enumerable.Range(0, active)
            .Select(_ => (PayslipDirtyFlag)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PayslipDirtyFlag)))
            .ToList();
        flags.ListActiveByPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(list));
        return flags;
    }

    private static IClosingChecklistGate Gate(bool openBlockers)
    {
        var gate = Substitute.For<IClosingChecklistGate>();
        gate.HasOpenBlockersBeforeApprovalAsync(Arg.Any<PayrollPeriod>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(openBlockers));
        return gate;
    }

    private static Sense.Shared.Contracts.Context.ICurrentUser CurrentUser(Guid? userId)
    {
        var user = Substitute.For<Sense.Shared.Contracts.Context.ICurrentUser>();
        user.UserId.Returns(userId);
        return user;
    }

    [Fact]
    public async Task Approve_IsRefused_WhileABlockingClosingCheckIsOpen()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(0), Gate(openBlockers: true), CurrentUser(Guid.NewGuid())).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodHasOpenBlockers);
        period.Status.ShouldBe(PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task Approve_MovesAnOpenPeriodToApproved()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(0), Gate(openBlockers: false), CurrentUser(Guid.NewGuid())).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(PayrollPeriodStatus.Approved);
    }

    [Fact]
    public async Task Approve_IsRefused_WhileChangedPayslipsAreNotRecalculated()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(2), Gate(openBlockers: false), CurrentUser(Guid.NewGuid())).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodHasDirtyPayslips);
        period.Status.ShouldBe(PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task Approve_IsRefused_WhenTheApproverAlsoRanTheLastCalculation()
    {
        var userId = Guid.NewGuid();
        var period = PeriodIn(PayrollPeriodStatus.Open);
        period.RecordCalculatedBy(userId);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(0), Gate(openBlockers: false), CurrentUser(userId)).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodApprovalNeedsDifferentUser);
        period.Status.ShouldBe(PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task Approve_Succeeds_WhenADifferentUserApprovesThanWhoCalculated()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);
        period.RecordCalculatedBy(Guid.NewGuid());

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(0), Gate(openBlockers: false), CurrentUser(Guid.NewGuid())).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(PayrollPeriodStatus.Approved);
    }

    [Fact]
    public async Task MarkPaid_AndLock_FollowTheForwardOrder()
    {
        var period = PeriodIn(PayrollPeriodStatus.Approved);
        var repository = Repository(period);

        (await new MarkPayrollPeriodPaidHandler(repository, Substitute.For<IPayslipRepository>(), Substitute.For<IIntegrationEventOutbox>())
            .Handle(new MarkPayrollPeriodPaidCommand(period.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new LockPayrollPeriodHandler(repository).Handle(new LockPayrollPeriodCommand(period.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        period.Status.ShouldBe(PayrollPeriodStatus.Locked);
    }

    [Fact]
    public async Task MarkPaid_PublishesPayrollPeriodPaid_WithEveryPayslip_SoEmployeesGetTheirPayslipByEmail()
    {
        var period = PeriodIn(PayrollPeriodStatus.Approved);
        var first = Payslip.CreateFirstVersion(TenantId, Guid.NewGuid(), period.Id, "{}", "h1", 100m, 80m, 130m);
        var second = Payslip.CreateFirstVersion(TenantId, Guid.NewGuid(), period.Id, "{}", "h2", 200m, 160m, 260m);
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns([first, second]);
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var result = await new MarkPayrollPeriodPaidHandler(Repository(period), payslips, outbox)
            .Handle(new MarkPayrollPeriodPaidCommand(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        outbox.Received(1).Enqueue(Arg.Is<PayrollPeriodPaid>(e =>
            e.PeriodId == period.Id && e.Year == 2026 && e.Month == 8 && e.Payslips.Count == 2
            && e.Payslips[0].PayslipId == first.Id && e.Payslips[1].EmployeeId == second.EmployeeId));
    }

    [Fact]
    public async Task MarkPaid_WhenRefused_PublishesNothing()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        var result = await new MarkPayrollPeriodPaidHandler(Repository(period), Substitute.For<IPayslipRepository>(), outbox)
            .Handle(new MarkPayrollPeriodPaidCommand(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        outbox.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task Lock_IsRefused_BeforeThePeriodIsPaid()
    {
        var period = PeriodIn(PayrollPeriodStatus.Approved);

        var result = await new LockPayrollPeriodHandler(Repository(period)).Handle(new LockPayrollPeriodCommand(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotPaid);
    }

    [Theory]
    [InlineData(PayrollPeriodStatus.Approved)]
    [InlineData(PayrollPeriodStatus.Paid)]
    [InlineData(PayrollPeriodStatus.Locked)]
    public void Reopen_SendsAnApprovedPaidOrLockedPeriodBackToOpen_KeepingTheReason(PayrollPeriodStatus status)
    {
        var period = PeriodIn(status);

        var result = period.Reopen("  Yanlış SGK günü düzeltilecek  ", Now);

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(PayrollPeriodStatus.Open);
        period.ReopenReason.ShouldBe("Yanlış SGK günü düzeltilecek");
        period.ReopenedAt.ShouldBe(Now);
    }

    [Fact]
    public void Reopen_RefusesAnOpenPeriod()
    {
        PeriodIn(PayrollPeriodStatus.Open).Reopen("neden", Now).Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotReopenable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reopen_NeedsAWrittenReason(string reason)
    {
        var period = PeriodIn(PayrollPeriodStatus.Approved);

        period.Reopen(reason, Now).Error.Code.ShouldBe(PayrollDomainErrors.PeriodReopenReasonInvalid);
        period.Status.ShouldBe(PayrollPeriodStatus.Approved);
    }

    [Fact]
    public async Task Reopen_Handler_ReportsAnUnknownPeriodAsNotFound()
    {
        var handler = new ReopenPayrollPeriodHandler(Substitute.For<IPayrollPeriodRepository>(), TimeProvider.System);

        var result = await handler.Handle(new ReopenPayrollPeriodCommand(Guid.NewGuid(), "neden"), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }
}
