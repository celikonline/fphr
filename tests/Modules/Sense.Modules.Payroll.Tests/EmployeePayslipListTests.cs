using NSubstitute;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>The employee card "Bordro" tab lists one employee's payslips with their periods, newest first.</summary>
public sealed class EmployeePayslipListTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static Payslip PayslipFor(Guid periodId, decimal gross, decimal net, decimal employerCost)
    {
        var payslip = (Payslip)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Payslip));
        typeof(Payslip).GetProperty(nameof(Payslip.PayrollPeriodId))!.SetValue(payslip, periodId);
        typeof(Payslip).GetProperty(nameof(Payslip.GrossAmount))!.SetValue(payslip, gross);
        typeof(Payslip).GetProperty(nameof(Payslip.NetAmount))!.SetValue(payslip, net);
        typeof(Payslip).GetProperty(nameof(Payslip.EmployerCost))!.SetValue(payslip, employerCost);
        return payslip;
    }

    private static ListEmployeePayslipsHandler Handler(IReadOnlyList<Payslip> mine, IReadOnlyList<PayrollPeriod> periods)
    {
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(mine));
        var periodRepository = Substitute.For<IPayrollPeriodRepository>();
        periodRepository.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(periods));
        return new ListEmployeePayslipsHandler(payslips, periodRepository);
    }

    [Fact]
    public async Task Handle_ReturnsThePayslipsNewestPeriodFirst_WithTheirAmounts()
    {
        var june = PayrollPeriod.Create(TenantId, 2026, 6).Value;
        var august = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        var handler = Handler(
            [PayslipFor(june.Id, 50_000m, 38_000m, 60_000m), PayslipFor(august.Id, 55_000m, 41_000m, 66_000m)],
            [june, august]);

        var result = await handler.Handle(new ListEmployeePayslipsQuery(EmployeeId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(p => (p.Year, p.Month)).ShouldBe([(2026, 8), (2026, 6)]);
        result.Value[0].GrossAmount.ShouldBe(55_000m);
        result.Value[0].NetAmount.ShouldBe(41_000m);
        result.Value[0].EmployerCost.ShouldBe(66_000m);
        result.Value[0].PeriodStatus.ShouldBe(PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task Handle_WhenTheEmployeeHasNoPayslips_ReturnsAnEmptyList()
    {
        var result = await Handler([], []).Handle(new ListEmployeePayslipsQuery(EmployeeId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsAPayslipWhoseOwnPeriodIsUnknown()
    {
        var june = PayrollPeriod.Create(TenantId, 2026, 6).Value;
        var handler = Handler([PayslipFor(Guid.NewGuid(), 1m, 1m, 1m), PayslipFor(june.Id, 2m, 2m, 2m)], [june]);

        var result = await handler.Handle(new ListEmployeePayslipsQuery(EmployeeId), CancellationToken.None);

        result.Value.Count.ShouldBe(1);
        result.Value[0].GrossAmount.ShouldBe(2m);
    }
}
