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
/// TK-030 kabul kriteri 2 ("Okudum, onaylıyorum"): <see cref="AcknowledgeMyPayslipHandler"/> is idempotent (a second
/// call keeps the first <see cref="PayslipDelivery.AcknowledgedAt"/>/version/evidence hash), only ever acknowledges
/// the CALLER's own payslip for a Paid/Locked period, and computes a deterministic <see cref="PayslipEvidenceHash"/>.
/// </summary>
public sealed class AcknowledgeMyPayslipCommandTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (IPayslipRepository Payslips, IPayrollPeriodRepository Periods, IPayslipDeliveryRepository Deliveries) Mocks() =>
        (Substitute.For<IPayslipRepository>(), Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipDeliveryRepository>());

    private static (Payslip Payslip, PayrollPeriod Period) PaidPayslip(Guid employeeId)
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "input-hash", 100_000m, 84_000m, 130_000m);
        return (payslip, period);
    }

    [Fact]
    public async Task Handle_FirstCall_CreatesDeliveryAndAcknowledges()
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, deliveries) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var (payslip, period) = PaidPayslip(employeeId);

        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        deliveries.FindByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDelivery?>(null));

        var handler = new AcknowledgeMyPayslipHandler(payslips, periods, deliveries, currentUser, TimeProvider.System);
        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(payslip.Id, PayslipDeliveryChannel.Mobile), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        deliveries.Received(1).Add(Arg.Is<PayslipDelivery>(d =>
            d.PayslipId == payslip.Id && d.EmployeeId == employeeId && d.AcknowledgedAt != null
            && d.Channel == PayslipDeliveryChannel.Mobile && d.PayslipVersion == payslip.Version
            && d.EvidenceHash == PayslipEvidenceHash.Compute(payslip.InputHash, payslip.Version)));
    }

    [Fact]
    public async Task Handle_SecondCall_IsIdempotent_KeepsTheFirstAcknowledgement()
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, deliveries) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var (payslip, period) = PaidPayslip(employeeId);

        var existingDelivery = PayslipDelivery.Create(TenantId, payslip.Id, employeeId, PayslipDeliveryChannel.Web);
        var firstAckAt = DateTimeOffset.UtcNow.AddDays(-1);
        var firstHash = PayslipEvidenceHash.Compute(payslip.InputHash, payslip.Version);
        existingDelivery.Acknowledge(firstAckAt, PayslipDeliveryChannel.Web, payslip.Version, firstHash);

        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        deliveries.FindByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDelivery?>(existingDelivery));

        var handler = new AcknowledgeMyPayslipHandler(payslips, periods, deliveries, currentUser, TimeProvider.System);
        // İkinci çağrı farklı bir kanalla gelse bile (ör. mobil) ilk onay değişmez.
        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(payslip.Id, PayslipDeliveryChannel.Mobile), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        existingDelivery.AcknowledgedAt.ShouldBe(firstAckAt);
        existingDelivery.Channel.ShouldBe(PayslipDeliveryChannel.Web);
        existingDelivery.EvidenceHash.ShouldBe(firstHash);
        deliveries.DidNotReceive().Add(Arg.Any<PayslipDelivery>());
    }

    [Fact]
    public async Task Handle_AnotherEmployeesPayslip_ReturnsNotFound_AndNeverTouchesDeliveries()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var (payslips, periods, deliveries) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var (othersPayslip, period) = PaidPayslip(otherEmployeeId);

        payslips.FindByIdAsync(othersPayslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(othersPayslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));

        var handler = new AcknowledgeMyPayslipHandler(payslips, periods, deliveries, currentUser, TimeProvider.System);
        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(othersPayslip.Id, PayslipDeliveryChannel.Web), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
        await deliveries.DidNotReceive().FindByPayslipIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PayrollPeriodStatus.Open)]
    [InlineData(PayrollPeriodStatus.Approved)]
    public async Task Handle_OwnPayslipInAStillChangeablePeriod_ReturnsNotFound(PayrollPeriodStatus status)
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, deliveries) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        if (status == PayrollPeriodStatus.Approved)
        {
            period.Approve(null);
        }

        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "input-hash", 100_000m, 84_000m, 130_000m);
        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));

        var handler = new AcknowledgeMyPayslipHandler(payslips, periods, deliveries, currentUser, TimeProvider.System);
        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(payslip.Id, PayslipDeliveryChannel.Web), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }

    [Fact]
    public async Task Handle_CallerHasNoEmployeeRecord_ReturnsNotFound()
    {
        var (payslips, periods, deliveries) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);

        var handler = new AcknowledgeMyPayslipHandler(payslips, periods, deliveries, currentUser, TimeProvider.System);
        var result = await handler.Handle(new AcknowledgeMyPayslipCommand(Guid.NewGuid(), PayslipDeliveryChannel.Web), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }
}

/// <summary><see cref="PayslipEvidenceHash"/> must be deterministic (same inputHash/version → same hash), so a
/// later re-verification of the evidentiary record can recompute and compare it.</summary>
public sealed class PayslipEvidenceHashTests
{
    [Fact]
    public void Compute_SameInputHashAndVersion_ReturnsTheSameHash()
    {
        var first = PayslipEvidenceHash.Compute("abc123", 2);
        var second = PayslipEvidenceHash.Compute("abc123", 2);

        first.ShouldBe(second);
    }

    [Fact]
    public void Compute_DifferentVersion_ReturnsADifferentHash()
    {
        var v1 = PayslipEvidenceHash.Compute("abc123", 1);
        var v2 = PayslipEvidenceHash.Compute("abc123", 2);

        v1.ShouldNotBe(v2);
    }
}
