using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Application.Severance;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

public sealed class SeveranceWageDateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly DateOnly Exit = new(2026, 9, 15);

    private static ILegislationParameterReader Legislation()
    {
        var decimals = new Dictionary<string, decimal>
        {
            [LegislationParameterKeys.SeveranceCeilingGross] = 100_000m,
            [LegislationParameterKeys.PayrollSgkFloor] = 30_000m,
            [LegislationParameterKeys.PayrollSgkCeiling] = 225_000m,
            [LegislationParameterKeys.PayrollSgkEmployeeRate] = 0.14m,
            [LegislationParameterKeys.PayrollUnemploymentEmployeeRate] = 0.01m,
            [LegislationParameterKeys.PayrollSgkEmployerRate] = 0.205m,
            [LegislationParameterKeys.PayrollUnemploymentEmployerRate] = 0.02m,
            [LegislationParameterKeys.PayrollSgkEmployerDiscountRate] = 0m,
            [LegislationParameterKeys.PayrollStampTaxRate] = 0.00759m,
            [LegislationParameterKeys.PayrollMinimumWageGross] = 30_000m,
            [LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption] = 0m,
            [LegislationParameterKeys.PayrollMealSgkDailyExemption] = 0m,
            [LegislationParameterKeys.PayrollMealMarketCardExemptionApplies] = 0m,
            [LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree1] = 12_000m,
            [LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree2] = 7_000m,
            [LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree3] = 3_000m,
        };
        var json = new Dictionary<string, string>
        {
            [LegislationParameterKeys.NoticeWeeksBySeniorityTiers] = """[{"upperBoundDays":183,"weeks":2},{"upperBoundDays":548,"weeks":4},{"upperBoundDays":1095,"weeks":6},{"upperBoundDays":null,"weeks":8}]""",
            [LegislationParameterKeys.PayrollIncomeTaxBrackets] = """[{"upperBound":158000,"rate":0.15},{"upperBound":330000,"rate":0.20},{"upperBound":1200000,"rate":0.27},{"upperBound":null,"rate":0.35}]""",
        };
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<LegislationParameterValue?>(decimals.TryGetValue((string)call[0], out var number)
                ? new((string)call[0], "TR", number, null, new DateOnly(2026, 1, 1), null, "test")
                : json.TryGetValue((string)call[0], out var text)
                    ? new((string)call[0], "TR", null, text, new DateOnly(2026, 1, 1), null, "test") : null));
        return reader;
    }

    private static SeveranceEstimator Build(EmployeeCurrentSalary? salary, out IEmployeeSalaryLookup salaries,
        out ILeaveBalanceLookup leaves, out IPayrollPeriodRepository periods, out IPayslipRepository payslips,
        ILegislationParameterReader? legislation = null, IEmployeePayrollProfileRepository? profileRepository = null)
    {
        salaries = Substitute.For<IEmployeeSalaryLookup>();
        salaries.GetSalaryAsOfAsync(Tenant, Employee, Exit, Arg.Any<CancellationToken>()).Returns(Task.FromResult(salary));
        salaries.GetCurrentSalaryAsync(Tenant, Employee, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(
            new(150_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, Exit.AddDays(1))));
        var seniority = Substitute.For<IEmployeeSeniorityLookup>();
        seniority.GetCountedSeniorityDaysAsync(Tenant, Employee, EmployeeSeniorityPurpose.Severance, Exit, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(400));
        leaves = Substitute.For<ILeaveBalanceLookup>();
        leaves.GetRemainingAnnualLeaveDaysAsOfAsync(Tenant, Employee, Exit, Arg.Any<CancellationToken>()).Returns(Task.FromResult<decimal?>(10m));
        periods = Substitute.For<IPayrollPeriodRepository>();
        payslips = Substitute.For<IPayslipRepository>();
        return new(salaries, seniority, leaves, legislation ?? Legislation(), TimeProvider.System,
            periods, payslips, Substitute.For<IEmployeeDisabilityLookup>(), profileRepository ?? Substitute.For<IEmployeePayrollProfileRepository>());
    }

    [Fact]
    public async Task Estimate_UsesTheExitDateSalaryAndLeaveBalance_NotCurrentValues()
    {
        var estimator = Build(new(60_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1)),
            out var salary, out var leave, out _, out _);

        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, SeveranceCauseCategory.EmployerNoticeBased, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DailyGrossWage.ShouldBe(2_000m);
        result.Value.UnusedLeaveAmount.ShouldBe(20_000m);
        result.Value.Steps.First().Code.ShouldBe("WAGE_BASIS");
        result.Value.Steps.First().Inputs["asOfDate"].ShouldBe("2026-09-15");
        await salary.DidNotReceive().GetCurrentSalaryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await leave.Received(1).GetRemainingAnnualLeaveDaysAsOfAsync(Tenant, Employee, Exit, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EmployeePayFrequency.Monthly, 45_000)]
    [InlineData(EmployeePayFrequency.Daily, 1_500)]
    [InlineData(EmployeePayFrequency.Hourly, 200)]
    public async Task NetWages_AreGrossedUpWithTheExitMonthsTaxParametersAndPriorBase(EmployeePayFrequency frequency, int netAmount)
    {
        var legislation = Legislation();
        var estimator = Build(new(netAmount, "TRY", EmployeeSalaryType.Net, frequency, new DateOnly(2026, 1, 1)),
            out _, out _, out var periods, out var payslips, legislation);
        var july = PayrollPeriod.Create(Tenant, 2026, 7).Value;
        periods.FindByYearMonthAsync(2026, 7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(july));
        payslips.FindByEmployeeAndPeriodAsync(Employee, july.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(
            Payslip.CreateFirstVersion(Tenant, Employee, july.Id, "{}", "hash", 50_000m, 35_000m, 60_000m, cumulativeGvBase: 500_000m)));

        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, SeveranceCauseCategory.EmployerNoticeBased, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var snapshot = await PayrollLegislationSnapshotResolver.ResolveAsync(legislation, Exit);
        var calculation = PayrollCalculator.Calculate(new PayrollCalculationInput(
            GrossMonthlySalary: result.Value.DailyGrossWage * 30m, WorkedDays: 30, CalendarDaysInMonth: 30,
            MealVariant: MealBenefitVariant.None, MealDailyAmount: 0m, PriorCumulativeGvBase: 500_000m, AsOfDate: Exit), snapshot);
        var monthlyNet = frequency == EmployeePayFrequency.Monthly ? netAmount : frequency == EmployeePayFrequency.Daily ? netAmount * 30m : netAmount * 225m;
        Math.Abs(NetToGrossSolver.SalaryNet(calculation) - monthlyNet).ShouldBeLessThanOrEqualTo(0.01m);
        result.Value.Steps.First().Inputs["priorCumulativeGvBase"].ShouldBe("500000");
    }

    [Fact]
    public async Task GrossHourlyWage_UsesTheSameDailyHoursBasisAsPayroll()
    {
        var estimator = Build(new(200m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Hourly, Exit), out _, out _, out _, out _);
        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, SeveranceCauseCategory.EmployerNoticeBased, CancellationToken.None);
        result.Value.DailyGrossWage.ShouldBe(1_500m);
    }

    [Fact]
    public async Task ACurrentSalaryCannotReplaceAMissingHistoricalSalary()
    {
        var estimator = Build(null, out _, out _, out _, out _);
        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, SeveranceCauseCategory.EmployerNoticeBased, CancellationToken.None);
        result.Error.Code.ShouldBe(PayrollDomainErrors.SeveranceEstimateSalaryMissing);
    }

    [Fact]
    public async Task MissingLeaveBalance_IsReportedInsteadOfAssumingZero()
    {
        var estimator = Build(new(60_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, Exit), out _, out var leaves, out _, out _);
        leaves.GetRemainingAnnualLeaveDaysAsOfAsync(Tenant, Employee, Exit, Arg.Any<CancellationToken>()).Returns(Task.FromResult<decimal?>(null));
        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, SeveranceCauseCategory.EmployerNoticeBased, CancellationToken.None);
        result.Error.Code.ShouldBe(PayrollDomainErrors.SeveranceEstimateLeaveBalanceMissing);
    }

    [Fact]
    public async Task InvalidExitCause_IsAValidationError_NotACalculatorException()
    {
        var estimator = Build(new(60_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, Exit), out _, out _, out _, out _);
        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, (SeveranceCauseCategory)999, CancellationToken.None);
        result.Error.Code.ShouldBe(PayrollDomainErrors.SeveranceEstimateCauseInvalid);
    }

    [Fact]
    public async Task RecurringBenefits_ComeFromTheExitDateProfile_AndStayOutOfUnusedLeavePay()
    {
        var profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        var profile = EmployeePayrollProfile.Create(Tenant, Employee, new EmployeePayrollProfileData(
            Guid.NewGuid(), null, new DateOnly(2026, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, null,
            MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null, false, null, null,
            RegularBenefitsMonthlyGross: 3_000m)).Value;
        profiles.FindCurrentAsync(Employee, Exit, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(profile));
        var estimator = Build(new(60_000m, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, Exit),
            out _, out _, out _, out _, profileRepository: profiles);

        var result = await estimator.EstimateAsync(Tenant, Employee, Exit, SeveranceCauseCategory.EmployerNoticeBased, CancellationToken.None);

        result.Value.RegularBenefitsMonthlyGross.ShouldBe(3_000m);
        result.Value.NoticeAmount.ShouldBe(58_800m);
        result.Value.UnusedLeaveAmount.ShouldBe(20_000m);
        result.Value.Steps.First().Inputs["regularBenefitsMonthlyGross"].ShouldBe("3000");
        await profiles.Received(1).FindCurrentAsync(Employee, Exit, Arg.Any<CancellationToken>());
    }
}
