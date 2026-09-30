using NSubstitute;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// TK-030 "Bordro Pusulam": <see cref="GetMyPayslipsHandler"/> only ever lists the CALLER's own payslips, and only
/// for periods already Paid or Locked - never another employee's payslips, never an Open/Approved (still-changeable)
/// period's payslip. Also carries each payslip's <see cref="PayslipDelivery"/> Viewed/Acknowledged state (kabul
/// kriteri 2/3) in the SAME query - no per-row lookup (N+1).
/// </summary>
public sealed class MyPayslipFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static GetMyPayslipsHandler Handler(
        IPayslipRepository payslips, IPayrollPeriodRepository periods, ICurrentUser currentUser, IPayslipDeliveryRepository? deliveries = null) =>
        new(payslips, periods, deliveries ?? EmptyDeliveries(), currentUser);

    private static IPayslipDeliveryRepository EmptyDeliveries()
    {
        var deliveries = Substitute.For<IPayslipDeliveryRepository>();
        deliveries.ListByEmployeeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayslipDelivery>>([]));
        return deliveries;
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsEmptyList()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);
        var handler = Handler(Substitute.For<IPayslipRepository>(), Substitute.For<IPayrollPeriodRepository>(), currentUser);

        var result = await handler.Handle(new GetMyPayslipsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsOnlyPaidAndLockedPeriods_NewestFirst()
    {
        var employeeId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var openPeriod = PayrollPeriod.Create(TenantId, 2026, 3).Value; // not visible: still open
        var paidPeriod = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        paidPeriod.Approve(null);
        paidPeriod.MarkPaid();
        var lockedPeriod = PayrollPeriod.Create(TenantId, 2026, 2).Value;
        lockedPeriod.Approve(null);
        lockedPeriod.MarkPaid();
        lockedPeriod.Lock();

        var openPayslip = Payslip.CreateFirstVersion(TenantId, employeeId, openPeriod.Id, "{}", "hash", 50_000m, 40_000m, 65_000m);
        var paidPayslip = Payslip.CreateFirstVersion(TenantId, employeeId, paidPeriod.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var lockedPayslip = Payslip.CreateFirstVersion(TenantId, employeeId, lockedPeriod.Id, "{}", "hash", 110_000m, 90_000m, 140_000m);

        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Payslip>>([openPayslip, paidPayslip, lockedPayslip]));
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([openPeriod, paidPeriod, lockedPeriod]));

        var handler = Handler(payslips, periods, currentUser);

        var result = await handler.Handle(new GetMyPayslipsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(p => (p.Year, p.Month)).ShouldBe([(2026, 2), (2026, 1)]);
        result.Value.ShouldAllBe(p => p.HasDocument);
        result.Value.ShouldNotContain(p => p.Month == 3);
    }

    [Fact]
    public async Task Handle_NeverIncludesAnotherEmployeesPayslips()
    {
        // Repository çağrısı zaten çağıranın kendi EmployeeId'siyle yapılıyor - başka çalışanın pusulası hiç sorgulanmıyor.
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();

        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([]));
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));

        var handler = Handler(payslips, periods, currentUser);

        var result = await handler.Handle(new GetMyPayslipsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await payslips.DidNotReceive().ListByEmployeeAsync(otherEmployeeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IncludesViewedAndAcknowledgedAt_FromOneBatchedDeliveryQuery()
    {
        var employeeId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);

        var delivery = PayslipDelivery.Create(TenantId, payslip.Id, employeeId, PayslipDeliveryChannel.Mobile);
        var viewedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var acknowledgedAt = DateTimeOffset.UtcNow.AddHours(-1);
        delivery.MarkViewed(viewedAt);
        delivery.Acknowledge(acknowledgedAt, PayslipDeliveryChannel.Mobile, payslip.Version, "evidence-hash");

        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip]));
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([period]));
        var deliveries = Substitute.For<IPayslipDeliveryRepository>();
        deliveries.ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayslipDelivery>>([delivery]));

        var handler = Handler(payslips, periods, currentUser, deliveries);

        var result = await handler.Handle(new GetMyPayslipsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().ViewedAt.ShouldBe(viewedAt);
        result.Value.Single().AcknowledgedAt.ShouldBe(acknowledgedAt);
        await deliveries.Received(1).ListByEmployeeAsync(employeeId, Arg.Any<CancellationToken>());
    }
}
