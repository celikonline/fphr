using NSubstitute;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Adjustments;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Adjustments;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Prim / komisyon: gross one-off earnings that can only be added or removed while the period is open.</summary>
public sealed class PayrollAdjustmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public void Create_TrimsTheLabel_AndKeepsTheMonth()
    {
        var created = PayrollAdjustment.Create(TenantId, EmployeeId, 2026, 8, PayrollAdjustmentKind.Bonus, "  Yıl sonu primi  ", 12_500m);

        created.IsSuccess.ShouldBeTrue();
        created.Value.Label.ShouldBe("Yıl sonu primi");
        (created.Value.Year, created.Value.Month).ShouldBe((2026, 8));
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("  ", 100)]
    [InlineData("Prim", 0)]
    [InlineData("Prim", -5)]
    public void Create_RejectsAMissingNameOrANonPositiveAmount(string label, int amount)
    {
        PayrollAdjustment.Create(TenantId, EmployeeId, 2026, 8, PayrollAdjustmentKind.Bonus, label, amount).Error.Code.ShouldBe(PayrollDomainErrors.AdjustmentInvalid);
    }

    [Fact]
    public void Create_RejectsAnInvalidMonth()
    {
        PayrollAdjustment.Create(TenantId, EmployeeId, 2026, 13, PayrollAdjustmentKind.Bonus, "Prim", 100m).IsFailure.ShouldBeTrue();
    }

    private static PayslipDirtyFlagService Flagging(IPayrollPeriodRepository periods)
    {
        periods.ListOpenCoveringOrAfterAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([]));
        return new PayslipDirtyFlagService(periods, Substitute.For<IPayslipDirtyFlagRepository>(), TimeProvider.System);
    }

    private static (AddPayrollAdjustmentHandler Handler, IPayrollAdjustmentRepository Adjustments) AddHandler(PayrollPeriod period)
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var adjustments = Substitute.For<IPayrollAdjustmentRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return (new AddPayrollAdjustmentHandler(periods, adjustments, Flagging(periods), tenant), adjustments);
    }

    [Fact]
    public async Task Add_StoresTheEarning_InAnOpenPeriod()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        var (handler, adjustments) = AddHandler(period);

        var result = await handler.Handle(new AddPayrollAdjustmentCommand(period.Id, EmployeeId, PayrollAdjustmentKind.Commission, "Satış komisyonu", 8_000m), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        adjustments.Received(1).Add(Arg.Is<PayrollAdjustment>(a => a.EmployeeId == EmployeeId && a.GrossAmount == 8_000m && a.Year == 2026 && a.Month == 8));
    }

    [Fact]
    public async Task Add_IsRefused_OnceThePeriodIsApproved()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        period.Approve();
        var (handler, adjustments) = AddHandler(period);

        var result = await handler.Handle(new AddPayrollAdjustmentCommand(period.Id, EmployeeId, PayrollAdjustmentKind.Bonus, "Prim", 1m), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.AdjustmentPeriodNotOpen);
        adjustments.DidNotReceive().Add(Arg.Any<PayrollAdjustment>());
    }

    [Fact]
    public async Task Remove_DeletesTheEarning_OnlyWhileThePeriodIsOpen()
    {
        var open = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        var adjustment = PayrollAdjustment.Create(TenantId, EmployeeId, 2026, 8, PayrollAdjustmentKind.Bonus, "Prim", 100m).Value;
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByYearMonthAsync(2026, 8, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(open));
        var adjustments = Substitute.For<IPayrollAdjustmentRepository>();
        adjustments.FindByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollAdjustment?>(adjustment));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var handler = new RemovePayrollAdjustmentHandler(periods, adjustments, Flagging(periods), tenant);

        (await handler.Handle(new RemovePayrollAdjustmentCommand(adjustment.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        adjustments.Received(1).Remove(adjustment);

        open.Approve();
        (await handler.Handle(new RemovePayrollAdjustmentCommand(adjustment.Id), CancellationToken.None)).Error.Code.ShouldBe(PayrollDomainErrors.AdjustmentPeriodNotOpen);
    }
}
