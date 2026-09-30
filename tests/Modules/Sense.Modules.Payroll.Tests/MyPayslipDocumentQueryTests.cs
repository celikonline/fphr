using NSubstitute;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// TK-030 "Bordro Pusulam" PDF: <see cref="GetMyPayslipDocumentHandler"/> only ever renders the CALLER's own payslip
/// for a Paid/Locked period; every other case (another employee's payslip, an Open/Approved period, no employee
/// record) returns the SAME not-found error, so an unauthorized caller cannot tell a payslip exists from a 404. A
/// successful render also opens/updates a <see cref="PayslipDelivery"/> row so <c>ViewedAt</c> is recorded (kabul
/// kriteri 3).
/// </summary>
public sealed class MyPayslipDocumentQueryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (IPayslipRepository Payslips, IPayrollPeriodRepository Periods, IPayslipDocumentProvider Documents, IPayslipDeliveryRepository Deliveries, IPayrollUnitOfWork UnitOfWork, ITenantContext Tenant) Mocks()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return (Substitute.For<IPayslipRepository>(), Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipDocumentProvider>(),
            Substitute.For<IPayslipDeliveryRepository>(), Substitute.For<IPayrollUnitOfWork>(), tenant);
    }

    private static GetMyPayslipDocumentHandler Handler(
        IPayslipRepository payslips, IPayrollPeriodRepository periods, IPayslipDocumentProvider documents,
        IPayslipDeliveryRepository deliveries, IPayrollUnitOfWork unitOfWork, ITenantContext tenant, ICurrentUser currentUser) =>
        new(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser, TimeProvider.System);

    [Fact]
    public async Task Handle_OwnPayslipInPaidPeriod_ReturnsTheFile()
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var file = new ExportFileResult("ucret-pusulasi_2026-01.pdf", "application/pdf", [1, 2, 3]);

        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        documents.RenderPdfAsync(TenantId, payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExportFileResult?>(file));
        deliveries.FindByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDelivery?>(null));

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(payslip.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(file);
    }

    [Fact]
    public async Task Handle_FirstDownload_OpensDeliveryAndSetsViewedAt()
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var file = new ExportFileResult("ucret-pusulasi_2026-01.pdf", "application/pdf", [1, 2, 3]);

        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        documents.RenderPdfAsync(TenantId, payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExportFileResult?>(file));
        deliveries.FindByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDelivery?>(null));

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(payslip.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        deliveries.Received(1).Add(Arg.Is<PayslipDelivery>(d => d.PayslipId == payslip.Id && d.EmployeeId == employeeId && d.ViewedAt != null));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SecondDownload_KeepsTheFirstViewedAt()
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var file = new ExportFileResult("ucret-pusulasi_2026-01.pdf", "application/pdf", [1, 2, 3]);
        var existingDelivery = PayslipDelivery.Create(TenantId, payslip.Id, employeeId, PayslipDeliveryChannel.Web);
        var firstViewedAt = DateTimeOffset.UtcNow.AddDays(-1);
        existingDelivery.MarkViewed(firstViewedAt);

        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        documents.RenderPdfAsync(TenantId, payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExportFileResult?>(file));
        deliveries.FindByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDelivery?>(existingDelivery));

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(payslip.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        existingDelivery.ViewedAt.ShouldBe(firstViewedAt);
        deliveries.DidNotReceive().Add(Arg.Any<PayslipDelivery>());
    }

    [Fact]
    public async Task Handle_AnotherEmployeesPayslip_ReturnsNotFound_AndNeverCallsTheDocumentProvider()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var othersPayslip = Payslip.CreateFirstVersion(TenantId, otherEmployeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);

        payslips.FindByIdAsync(othersPayslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(othersPayslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(othersPayslip.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
        await documents.DidNotReceive().RenderPdfAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await deliveries.DidNotReceive().FindByPayslipIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PayrollPeriodStatus.Open)]
    [InlineData(PayrollPeriodStatus.Approved)]
    public async Task Handle_OwnPayslipInAStillChangeablePeriod_ReturnsNotFound(PayrollPeriodStatus status)
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        if (status == PayrollPeriodStatus.Approved)
        {
            period.Approve(null);
        }

        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        payslips.FindByIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(payslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(payslip.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }

    [Fact]
    public async Task Handle_UnknownPayslipId_ReturnsNotFound()
    {
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(Guid.NewGuid());
        payslips.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(null));

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }

    [Fact]
    public async Task Handle_CallerHasNoEmployeeRecord_ReturnsNotFound()
    {
        var (payslips, periods, documents, deliveries, unitOfWork, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);

        var handler = Handler(payslips, periods, documents, deliveries, unitOfWork, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }
}
