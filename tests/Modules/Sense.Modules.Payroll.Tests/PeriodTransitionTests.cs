using NSubstitute;
using Sense.Modules.Payroll.Application.Closing;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
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
        if (status >= PayrollPeriodStatus.Approved) period.Approve();
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

    [Fact]
    public async Task Approve_IsRefused_WhileABlockingClosingCheckIsOpen()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(0), Gate(openBlockers: true)).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodHasOpenBlockers);
        period.Status.ShouldBe(PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task Approve_MovesAnOpenPeriodToApproved()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(0), Gate(openBlockers: false)).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(PayrollPeriodStatus.Approved);
    }

    [Fact]
    public async Task Approve_IsRefused_WhileChangedPayslipsAreNotRecalculated()
    {
        var period = PeriodIn(PayrollPeriodStatus.Open);

        var result = await new ApprovePayrollPeriodHandler(Repository(period), DirtyFlags(2), Gate(openBlockers: false)).Handle(new ApprovePayrollPeriodCommand(period.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodHasDirtyPayslips);
        period.Status.ShouldBe(PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task MarkPaid_AndLock_FollowTheForwardOrder()
    {
        var period = PeriodIn(PayrollPeriodStatus.Approved);
        var repository = Repository(period);

        (await new MarkPayrollPeriodPaidHandler(repository).Handle(new MarkPayrollPeriodPaidCommand(period.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new LockPayrollPeriodHandler(repository).Handle(new LockPayrollPeriodCommand(period.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        period.Status.ShouldBe(PayrollPeriodStatus.Locked);
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
