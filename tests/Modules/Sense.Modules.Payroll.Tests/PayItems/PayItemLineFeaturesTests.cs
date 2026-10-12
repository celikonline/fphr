using System.Reflection;
using System.Text.Json;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>GET /periods/{id}/pay-item-lines (PAY-FORMULA-3): lines come from each payslip's latest PAY_ITEM trace steps.</summary>
public sealed class PayItemLineFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static TraceStep PayItemStep(int order, string code, string name, decimal amount, decimal? netTarget = null)
    {
        var inputs = new Dictionary<string, string>
        {
            ["code"] = code,
            ["name"] = name,
            ["amountBasis"] = netTarget is null ? "gross" : "net",
        };
        if (netTarget is { } net)
        {
            inputs["netTarget"] = net.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new TraceStep(order, PayrollTraceStepCodes.PayItem, "payroll.trace.step.pay_item", inputs, "deger", amount, [], code);
    }

    private static TraceStep Plain(int order, string code) => new(order, code, "t", new Dictionary<string, string>(), "f", 1m, []);

    private static (ListPeriodPayItemLinesHandler Handler, PayrollPeriod Period, Guid EmployeeWith, Guid EmployeeWithout) Arrange()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 9).Value;
        var withItems = Guid.NewGuid();
        var withoutItems = Guid.NewGuid();
        var slipWith = Payslip.CreateFirstVersion(TenantId, withItems, period.Id, "{}", "h", 100m, 80m, 120m);
        var slipWithout = Payslip.CreateFirstVersion(TenantId, withoutItems, period.Id, "{}", "h", 100m, 80m, 120m);

        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([slipWith, slipWithout]));
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        List<TraceStep> stepsWith = [Plain(1, PayrollTraceStepCodes.Days), PayItemStep(2, "YEMEK", "Yemek yardımı", 1_500m), PayItemStep(3, "NETPRIM", "Net prim", 12_345.67m, 10_000m), Plain(4, PayrollTraceStepCodes.Earnings)];
        traces.FindLatestByPayslipIdAsync(slipWith.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PayslipCalculationTrace?>(PayslipCalculationTrace.Create(TenantId, slipWith.Id, Guid.NewGuid(), 1, JsonSerializer.Serialize(stepsWith, Json), Guid.NewGuid(), DateTime.UtcNow)));
        List<TraceStep> stepsWithout = [Plain(1, PayrollTraceStepCodes.Days), Plain(2, PayrollTraceStepCodes.Earnings)];
        traces.FindLatestByPayslipIdAsync(slipWithout.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PayslipCalculationTrace?>(PayslipCalculationTrace.Create(TenantId, slipWithout.Id, Guid.NewGuid(), 1, JsonSerializer.Serialize(stepsWithout, Json), Guid.NewGuid(), DateTime.UtcNow)));
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(withItems, "Ada", "Yılmaz", null, null, null, "Active")]));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return (new ListPeriodPayItemLinesHandler(periods, payslips, traces, directory, tenant), period, withItems, withoutItems);
    }

    [Fact]
    public async Task Handle_ReturnsOneLinePerPayItemStep_OnlyForEmployeesThatHaveThem()
    {
        var (handler, period, employeeWith, _) = Arrange();

        var result = await handler.Handle(new ListPeriodPayItemLinesQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value[0].ShouldBe(new PayItemLineDto(employeeWith, "Ada Yılmaz", "YEMEK", "Yemek yardımı", 1_500m, "gross"));
        result.Value[1].ShouldBe(new PayItemLineDto(employeeWith, "Ada Yılmaz", "NETPRIM", "Net prim", 12_345.67m, "net", 10_000m));
    }

    [Fact]
    public async Task Handle_SerializesLikeTheContract_NullNetTargetIsOmitted()
    {
        var (handler, period, _, _) = Arrange();
        var result = await handler.Handle(new ListPeriodPayItemLinesQuery(period.Id), CancellationToken.None);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var first = JsonSerializer.Serialize(result.Value[0], options);
        var second = JsonSerializer.Serialize(result.Value[1], options);

        first.ShouldNotContain("netTarget");
        first.ShouldContain("\"amountBasis\":\"gross\"");
        second.ShouldContain("\"netTarget\":10000");
    }

    [Fact]
    public async Task Handle_UnknownPeriod_IsNotFound()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(null));
        var handler = new ListPeriodPayItemLinesHandler(
            periods, Substitute.For<IPayslipRepository>(), Substitute.For<IPayslipCalculationTraceRepository>(),
            Substitute.For<IEmployeeDirectory>(), Substitute.For<ITenantContext>());

        var result = await handler.Handle(new ListPeriodPayItemLinesQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotFound);
    }

    [Fact]
    public void Query_RequiresTheTraceViewPermission()
    {
        typeof(ListPeriodPayItemLinesQuery).GetCustomAttributes<RequiresPermissionAttribute>().ShouldHaveSingleItem().Permission.ShouldBe("payroll.trace.view");
    }
}
