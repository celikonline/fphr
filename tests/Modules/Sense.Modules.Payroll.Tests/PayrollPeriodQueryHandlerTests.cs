using System.Text.Json;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// <see cref="ListPayrollPeriodsHandler"/>/<see cref="GetPayrollPeriodHandler"/> (faz-07 tur 1: read-only period
/// list/detail for FE's periods.tsx/period-detail.tsx, contract agreed with the FE twin's payroll.service.ts note;
/// tur 2 madde 6/7: <see cref="PayslipLineDto.WorkedDays"/>/<see cref="PayslipLineDto.CalendarDaysInMonth"/> read
/// from the persisted DAYS trace step).
/// </summary>
public sealed class PayrollPeriodQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static string DaysStepJson(int workedDays, int calendarDaysInMonth)
    {
        var steps = new List<TraceStep>
        {
            new(
                1, PayrollTraceStepCodes.Days, "payroll.trace.step.days",
                new Dictionary<string, string>
                {
                    ["workedDays"] = workedDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["calendarDaysInMonth"] = calendarDaysInMonth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                "f", workedDays, []),
        };
        return JsonSerializer.Serialize(steps, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

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
        var handler = new GetPayrollPeriodHandler(
            periods, Substitute.For<IPayslipRepository>(), Substitute.For<IPayslipCalculationTraceRepository>(),
            Substitute.For<IEmployeeDirectory>(), Substitute.For<IEmployeeCostCenterReader>(), Substitute.For<ITenantContext>());

        var result = await handler.Handle(new GetPayrollPeriodQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotFound);
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_ReturnsHeaderAndPayslipLines()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var payslips = Substitute.For<IPayslipRepository>();
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var costCenterReader = Substitute.For<IEmployeeCostCenterReader>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var employeeId = Guid.NewGuid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip]));
        traces.FindLatestByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipCalculationTrace?>(null));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(employeeId, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active")]));

        var handler = new GetPayrollPeriodHandler(periods, payslips, traces, employeeDirectory, costCenterReader, tenant);

        var result = await handler.Handle(new GetPayrollPeriodQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Year.ShouldBe(2026);
        result.Value.Month.ShouldBe(3);
        result.Value.Payslips.Count.ShouldBe(1);
        result.Value.Payslips[0].EmployeeName.ShouldBe("Ada Yilmaz");
        result.Value.Payslips[0].GrossAmount.ShouldBe(100_000m);
        result.Value.Payslips[0].NetAmount.ShouldBe(84_000m);
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_PayslipWithTrace_ReturnsWorkedDaysAndCalendarDaysFromDaysStep()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var payslips = Substitute.For<IPayslipRepository>();
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var costCenterReader = Substitute.For<IEmployeeCostCenterReader>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var employeeId = Guid.NewGuid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var trace = PayslipCalculationTrace.Create(TenantId, payslip.Id, Guid.NewGuid(), 1, DaysStepJson(27, 30), Guid.NewGuid(), DateTime.UtcNow);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip]));
        traces.FindLatestByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipCalculationTrace?>(trace));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(employeeId, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active")]));

        var handler = new GetPayrollPeriodHandler(periods, payslips, traces, employeeDirectory, costCenterReader, tenant);

        var result = await handler.Handle(new GetPayrollPeriodQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payslips[0].WorkedDays.ShouldBe(27);
        result.Value.Payslips[0].CalendarDaysInMonth.ShouldBe(30);
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_PayslipWithoutTrace_ReturnsNullWorkedDaysAndCalendarDays()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var payslips = Substitute.For<IPayslipRepository>();
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var costCenterReader = Substitute.For<IEmployeeCostCenterReader>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var employeeId = Guid.NewGuid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip]));
        traces.FindLatestByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipCalculationTrace?>(null));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(employeeId, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active")]));

        var handler = new GetPayrollPeriodHandler(periods, payslips, traces, employeeDirectory, costCenterReader, tenant);

        var result = await handler.Handle(new GetPayrollPeriodQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payslips[0].WorkedDays.ShouldBeNull();
        result.Value.Payslips[0].CalendarDaysInMonth.ShouldBeNull();
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_WithoutCostCenterFilter_ReturnsAllPayslips()
    {
        // Faz-07 tur 1 madde 1: filtre parametresi yoksa tüm dönem çalışanları dönmeli.
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var payslips = Substitute.For<IPayslipRepository>();
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var costCenterReader = Substitute.For<IEmployeeCostCenterReader>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        var payslip1 = Payslip.CreateFirstVersion(TenantId, employeeId1, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var payslip2 = Payslip.CreateFirstVersion(TenantId, employeeId2, period.Id, "{}", "hash", 80_000m, 68_000m, 100_000m);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip1, payslip2]));
        traces.FindLatestByPayslipIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipCalculationTrace?>(null));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([
                new EmployeeContact(employeeId1, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active"),
                new EmployeeContact(employeeId2, "Bob", "Ozcan", "bob@acme.com", null, null, "Active"),
            ]));

        var handler = new GetPayrollPeriodHandler(periods, payslips, traces, employeeDirectory, costCenterReader, tenant);

        var result = await handler.Handle(new GetPayrollPeriodQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payslips.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetPayrollPeriodHandler_WithCostCenterFilter_ReturnsOnlyFilteredPayslips()
    {
        // Faz-07 tur 1 madde 1: costCenterId filtesi uygulanırsa yalnız o merkeze atanan çalışanlar dönmeli.
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var payslips = Substitute.For<IPayslipRepository>();
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var costCenterReader = Substitute.For<IEmployeeCostCenterReader>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var period = PayrollPeriod.Create(TenantId, 2026, 3).Value;
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        var costCenterId = Guid.NewGuid();
        var payslip1 = Payslip.CreateFirstVersion(TenantId, employeeId1, period.Id, "{}", "hash", 100_000m, 84_000m, 130_000m);
        var payslip2 = Payslip.CreateFirstVersion(TenantId, employeeId2, period.Id, "{}", "hash", 80_000m, 68_000m, 100_000m);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip1, payslip2]));
        traces.FindLatestByPayslipIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipCalculationTrace?>(null));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([
                new EmployeeContact(employeeId1, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active"),
                new EmployeeContact(employeeId2, "Bob", "Ozcan", "bob@acme.com", null, null, "Active"),
            ]));

        // Stub: only employee 1 is in the requested cost center (real impl from faz-02)
        costCenterReader.GetEmployeeCostCenterMappingAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
                new Dictionary<Guid, Guid> { { employeeId1, costCenterId } }));

        var handler = new GetPayrollPeriodHandler(periods, payslips, traces, employeeDirectory, costCenterReader, tenant);

        var result = await handler.Handle(new GetPayrollPeriodQuery(period.Id, costCenterId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Payslips.Count.ShouldBe(1);
        result.Value.Payslips[0].EmployeeName.ShouldBe("Ada Yilmaz");
    }
}
