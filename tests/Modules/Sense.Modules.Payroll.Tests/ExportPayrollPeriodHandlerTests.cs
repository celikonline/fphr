using System.Text.Json;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// <see cref="ExportPayrollPeriodHandler"/> (K-7 bordro Excel özeti, faz-07 tur 1 madde 4, S-14): repository/exporter
/// ports are NSubstitute fakes (mirrors PayrollPeriodCalculationServiceTests), no DB - what's under test is the
/// row-shape (sicil/ad soyad/brüt/SGK işçi/gelir vergisi/damga/net/masraf iadesi) and the tenant-isolation 404.
/// </summary>
public sealed class ExportPayrollPeriodHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static PayrollPeriod OpenPeriod() => PayrollPeriod.Create(TenantId, 2026, 3).Value;

    private static ExportPayrollPeriodHandler BuildHandler(
        out IPayrollPeriodRepository periods, out IPayslipRepository payslips, out IPayslipCalculationTraceRepository traces,
        out IEmployeePayrollProfileRepository profiles, out IEmployeeDirectory employeeDirectory, out IReportExporter exporter)
    {
        periods = Substitute.For<IPayrollPeriodRepository>();
        payslips = Substitute.For<IPayslipRepository>();
        traces = Substitute.For<IPayslipCalculationTraceRepository>();
        profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        employeeDirectory = Substitute.For<IEmployeeDirectory>();
        exporter = Substitute.For<IReportExporter>();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        return new ExportPayrollPeriodHandler(periods, payslips, traces, profiles, employeeDirectory, tenant, exporter);
    }

    private static string StepsJson(decimal sgkEmployee, decimal incomeTax, decimal minWageExemptionGv, decimal stampTax, decimal minWageExemptionDv, decimal expenseReimbursement)
    {
        var steps = new List<TraceStep>
        {
            new(1, PayrollTraceStepCodes.SgkEmployee, "t", new Dictionary<string, string>(), "f", sgkEmployee, []),
            new(2, PayrollTraceStepCodes.IncomeTax, "t", new Dictionary<string, string>(), "f", incomeTax, []),
            new(3, PayrollTraceStepCodes.MinWageExemptionGv, "t", new Dictionary<string, string>(), "f", minWageExemptionGv, []),
            new(4, PayrollTraceStepCodes.StampTax, "t", new Dictionary<string, string>(), "f", stampTax, []),
            new(5, PayrollTraceStepCodes.MinWageExemptionDv, "t", new Dictionary<string, string>(), "f", minWageExemptionDv, []),
            new(6, PayrollTraceStepCodes.Net, "t", new Dictionary<string, string> { ["expenseReimbursement"] = expenseReimbursement.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) }, "f", 0m, []),
        };
        return JsonSerializer.Serialize(steps, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task Handle_UnknownOrOtherTenantPeriod_ReturnsNotFound()
    {
        var handler = BuildHandler(out var periods, out _, out _, out _, out _, out _);
        periods.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(null));

        var result = await handler.Handle(new ExportPayrollPeriodQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayrollDomainErrors.PeriodNotFound);
    }

    [Fact]
    public async Task Handle_BuildsOneRowPerPayslip_WithAfterExemptionTaxAndExpenseReimbursement()
    {
        var handler = BuildHandler(out var periods, out var payslips, out var traces, out var profiles, out var employeeDirectory, out var exporter);
        var period = OpenPeriod();
        var employeeId = Guid.NewGuid();
        var payslip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "hash", grossAmount: 100_000m, netAmount: 84_000m, employerCost: 130_000m);
        var trace = PayslipCalculationTrace.Create(
            TenantId, payslip.Id, Guid.NewGuid(), 1,
            StepsJson(sgkEmployee: 14_000m, incomeTax: 13_500m, minWageExemptionGv: 3_825m, stampTax: 759m, minWageExemptionDv: 227.70m, expenseReimbursement: 2_500m),
            Guid.NewGuid(), DateTime.UtcNow);

        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([payslip]));
        traces.FindLatestByPayslipIdAsync(payslip.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipCalculationTrace?>(trace));
        employeeDirectory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(employeeId, "Ada", "Yilmaz", "ada@acme.com", null, null, "Active")]));
        var profile = EmployeePayrollProfile.Create(
            TenantId, employeeId,
            new EmployeePayrollProfileData(
                Guid.NewGuid(), null, new DateOnly(2026, 1, 1), null,
                WageBasis.Monthly, SgkStatus.Normal, null,
                MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null,
                SickDaysReducePay: false, ExternalPayrollCode: "SCL-042", Notes: null)).Value;
        profiles.FindCurrentAsync(employeeId, new DateOnly(2026, 3, 1), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(profile));

        ExportTable? captured = null;
        exporter.ExportToExcel(Arg.Do<ExportTable>(t => captured = t), Arg.Any<CancellationToken>())
            .Returns(new ExportFileResult("bordro.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", []));

        var result = await handler.Handle(new ExportPayrollPeriodQuery(period.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Rows.Count.ShouldBe(1);
        var row = captured.Rows[0];
        row[0].ShouldBe("SCL-042"); // sicil (ExternalPayrollCode)
        row[1].ShouldBe("Ada Yilmaz"); // ad soyad
        row[2].ShouldBe(100_000m); // brüt
        row[3].ShouldBe(14_000m); // SGK işçi
        row[4].ShouldBe(9_675m); // gelir vergisi = 13,500 - 3,825 (istisna sonrası)
        row[5].ShouldBe(531.30m); // damga = 759 - 227.70 (istisna sonrası)
        row[6].ShouldBe(84_000m); // net
        row[7].ShouldBe(2_500m); // masraf iadesi
    }
}
