using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// <see cref="ListPayrollPeriodsHandler"/>/<see cref="GetPayrollPeriodHandler"/> (faz-07 tur 1: read-only period
/// list/detail for FE's periods.tsx/period-detail.tsx, contract agreed with the FE twin's payroll.service.ts note).
/// </summary>
public sealed class PayrollPeriodQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task ListPayrollPeriodsHandler_MapsEveryPeriodToADto()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var periodA = PayrollPeriod.Create(TenantId, 2026, 2).Value;
        var periodB = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        periods.ListAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayrollPeriod>>([periodB, periodA]));
        var handler = new ListPayrollPeriodsHandler(periods);

        var result = await handler.Handle(new ListPayrollPeriodsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(p => (p.Year, p.Month)).ShouldBe([(2026, 3), (2026, 2)]);
        result.Value.Select(p => p.Status).ShouldAllBe(s => s == PayrollPeriodStatus.Open);
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_UnknownOrOtherTenantPeriod_ReturnsNotFound()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(null));
        var handler = new GetPayrollPeriodHandler(periods, Substitute.For<IPayslipRepository>(), Substitute.For<IEmployeeDirectory>(), Substitute.For<ITenantContext>());

        var result = await handler.Handle(new GetPayrollPeriodQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotFound);
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_ReturnsHeaderAndPayslipLines()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var payslips = Substitute.For<IPayslipRepository>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var employeeId = Guid.NewGuid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip]));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(employeeId, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active")]));

        var handler = new GetPayrollPeriodHandler(periods, payslips, employeeDirectory, tenant);

        var result = await handler.Handle(new GetPayrollPeriodQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Year.ShouldBe(2026);
        result.Value.Month.ShouldBe(3);
        result.Value.Payslips.Count.ShouldBe(1);
        result.Value.Payslips[0].EmployeeName.ShouldBe("Ada Yilmaz");
        result.Value.Payslips[0].GrossAmount.ShouldBe(100_000m);
        result.Value.Payslips[0].NetAmount.ShouldBe(84_000m);
    }
}
