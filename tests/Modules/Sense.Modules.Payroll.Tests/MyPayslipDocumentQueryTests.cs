using NSubstitute;
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
/// record) returns the SAME not-found error, so an unauthorized caller cannot tell a payslip exists from a 404.
/// </summary>
public sealed class MyPayslipDocumentQueryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (IPayslipRepository Payslips, IPayrollPeriodRepository Periods, IPayslipDocumentProvider Documents, ITenantContext Tenant) Mocks()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return (Substitute.For<IPayslipRepository>(), Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IPayslipDocumentProvider>(), tenant);
    }

    [Fact]
    public async Task Handle_OwnPayslipInPaidPeriod_ReturnsTheFile()
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, documents, tenant) = Mocks();
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

        var handler = new GetMyPayslipDocumentHandler(payslips, periods, documents, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(payslip.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(file);
    }

    [Fact]
    public async Task Handle_AnotherEmployeesPayslip_ReturnsNotFound_AndNeverCallsTheDocumentProvider()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var (payslips, periods, documents, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);

        var period = PayrollPeriod.Create(TenantId, 2026, 1).Value;
        period.Approve(null);
        period.MarkPaid();
        var othersPayslip = Payslip.CreateFirstVersion(TenantId, otherEmployeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);

        payslips.FindByIdAsync(othersPayslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(othersPayslip));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));

        var handler = new GetMyPayslipDocumentHandler(payslips, periods, documents, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(othersPayslip.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
        await documents.DidNotReceive().RenderPdfAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PayrollPeriodStatus.Open)]
    [InlineData(PayrollPeriodStatus.Approved)]
    public async Task Handle_OwnPayslipInAStillChangeablePeriod_ReturnsNotFound(PayrollPeriodStatus status)
    {
        var employeeId = Guid.NewGuid();
        var (payslips, periods, documents, tenant) = Mocks();
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

        var handler = new GetMyPayslipDocumentHandler(payslips, periods, documents, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(payslip.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }

    [Fact]
    public async Task Handle_UnknownPayslipId_ReturnsNotFound()
    {
        var (payslips, periods, documents, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(Guid.NewGuid());
        payslips.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(null));

        var handler = new GetMyPayslipDocumentHandler(payslips, periods, documents, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }

    [Fact]
    public async Task Handle_CallerHasNoEmployeeRecord_ReturnsNotFound()
    {
        var (payslips, periods, documents, tenant) = Mocks();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);

        var handler = new GetMyPayslipDocumentHandler(payslips, periods, documents, tenant, currentUser);
        var result = await handler.Handle(new GetMyPayslipDocumentQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PayslipNotFound);
    }
}
