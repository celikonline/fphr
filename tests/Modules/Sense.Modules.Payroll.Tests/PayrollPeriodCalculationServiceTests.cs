using System.Text.Json;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// <see cref="PayrollPeriodCalculationService"/> (Faz 3 P3 "Dönem hesabı" / "Değişenleri yeniden hesapla"): repository
/// and cross-module ports are NSubstitute fakes (mirrors Sense.Modules.Documents.Tests' handler-level tests), no DB -
/// the orchestration logic (which employees get calculated, dirty-flag clearing) is what's under test, not persistence.
/// </summary>
public sealed class PayrollPeriodCalculationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SgkWorkplaceCompanyId = Guid.NewGuid();
    private static readonly DateOnly AsOf = new(2026, 3, 1);

    private static PayrollPeriod OpenPeriod() => PayrollPeriod.Create(TenantId, 2026, 3).Value;

    private static EmployeePayrollProfile Profile(Guid employeeId) => EmployeePayrollProfile.Create(
        TenantId, employeeId,
        new EmployeePayrollProfileData(
            SgkWorkplaceCompanyId, null, new DateOnly(2026, 1, 1), null,
            WageBasis.Monthly, SgkStatus.Normal, null,
            MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null,
            SickDaysReducePay: false, ExternalPayrollCode: null, Notes: null)).Value;

    private static EmployeePayrollProfile ProfileWithTransport(Guid employeeId, TransportVariant variant, decimal dailyAmount) => EmployeePayrollProfile.Create(
        TenantId, employeeId,
        new EmployeePayrollProfileData(
            SgkWorkplaceCompanyId, null, new DateOnly(2026, 1, 1), null,
            WageBasis.Monthly, SgkStatus.Normal, null,
            MealBenefitVariant.None, 0m, variant, PayrollPaymentMethod.Bank, null,
            SickDaysReducePay: false, ExternalPayrollCode: null, Notes: null, TransportDailyAmount: dailyAmount)).Value;

    private static EmployeeCurrentSalary Salary(decimal amount = 50_000m) =>
        new(amount, "TRY", EmployeeSalaryType.Gross, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1));

    /// <summary>Legislation reader stub returning the same golden-set values PayrollCalculatorGoldenSetTests uses,
    /// so PayrollLegislationSnapshotResolver.ResolveAsync succeeds for any key it asks for.</summary>
    private static ILegislationParameterReader Legislation()
    {
        var values = new Dictionary<string, LegislationParameterValue>
        {
            [LegislationParameterKeys.PayrollIncomeTaxBrackets] = Json(LegislationParameterKeys.PayrollIncomeTaxBrackets,
                """[{"upperBound":158000,"rate":0.15},{"upperBound":330000,"rate":0.20},{"upperBound":1200000,"rate":0.27},{"upperBound":4300000,"rate":0.35},{"upperBound":null,"rate":0.40}]"""),
            [LegislationParameterKeys.PayrollSgkFloor] = Decimal(LegislationParameterKeys.PayrollSgkFloor, 30_000m),
            [LegislationParameterKeys.PayrollSgkCeiling] = Decimal(LegislationParameterKeys.PayrollSgkCeiling, 225_000m),
            [LegislationParameterKeys.PayrollSgkEmployeeRate] = Decimal(LegislationParameterKeys.PayrollSgkEmployeeRate, 0.14m),
            [LegislationParameterKeys.PayrollUnemploymentEmployeeRate] = Decimal(LegislationParameterKeys.PayrollUnemploymentEmployeeRate, 0.01m),
            [LegislationParameterKeys.PayrollSgkEmployerRate] = Decimal(LegislationParameterKeys.PayrollSgkEmployerRate, 0.205m),
            [LegislationParameterKeys.PayrollUnemploymentEmployerRate] = Decimal(LegislationParameterKeys.PayrollUnemploymentEmployerRate, 0.02m),
            [LegislationParameterKeys.PayrollSgkEmployerDiscountRate] = Decimal(LegislationParameterKeys.PayrollSgkEmployerDiscountRate, 0.05m),
            [LegislationParameterKeys.PayrollStampTaxRate] = Decimal(LegislationParameterKeys.PayrollStampTaxRate, 0.00759m),
            [LegislationParameterKeys.PayrollMinimumWageGross] = Decimal(LegislationParameterKeys.PayrollMinimumWageGross, 30_000m),
            [LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption] = Decimal(LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption, 240m),
            [LegislationParameterKeys.PayrollMealSgkDailyExemptionRate] = Decimal(LegislationParameterKeys.PayrollMealSgkDailyExemptionRate, 0.2365m),
            [LegislationParameterKeys.PayrollMealMarketCardExemptionApplies] = Decimal(LegislationParameterKeys.PayrollMealMarketCardExemptionApplies, 0m),
            [LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree1] = Decimal(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree1, 6900m),
            [LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree2] = Decimal(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree2, 4000m),
            [LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree3] = Decimal(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree3, 1700m),
        };

        var reader = Substitute.For<ILegislationParameterReader>();
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(values.GetValueOrDefault((string)ci[0])));
        return reader;
    }

    private static LegislationParameterValue Decimal(string key, decimal value) =>
        new(key, LegislationCountries.Turkey, value, null, new DateOnly(2026, 1, 1), null, "test");

    private static LegislationParameterValue Json(string key, string json) =>
        new(key, LegislationCountries.Turkey, null, json, new DateOnly(2026, 1, 1), null, "test");

    private static PayrollPeriodCalculationService BuildService(
        out IPayslipRepository payslips, out IPayslipCalculationTraceRepository traces,
        out IEmployeePayrollProfileRepository profiles, out IPayslipDirtyFlagRepository dirtyFlags,
        out IEmployeeSalaryLookup salaryLookup) =>
        BuildService(out payslips, out traces, out profiles, out dirtyFlags, out salaryLookup, out _);

    /// <summary>Overload exposing the IWorkedTimeReader fake (TB-7 port, tur 3): default NSubstitute behaviour
    /// (GetAsync returns null for any Arg) already reproduces "no data" without any .Returns() setup, which is why
    /// every other test in this file keeps using the 5-out overload above unchanged.</summary>
    private static PayrollPeriodCalculationService BuildService(
        out IPayslipRepository payslips, out IPayslipCalculationTraceRepository traces,
        out IEmployeePayrollProfileRepository profiles, out IPayslipDirtyFlagRepository dirtyFlags,
        out IEmployeeSalaryLookup salaryLookup, out IWorkedTimeReader workedTimeReader) =>
        BuildService(out payslips, out traces, out profiles, out dirtyFlags, out salaryLookup, out workedTimeReader, out _);

    /// <summary>Overload additionally exposing the IExpensePayoutReader fake (TB-7 Expense köprüsü, faz-07 tur 1
    /// madde 3): default NSubstitute behaviour (GetAsync returns null) reproduces "no reimbursement this month"
    /// without any .Returns() setup, so every other test in this file is unaffected.</summary>
    private static PayrollPeriodCalculationService BuildService(
        out IPayslipRepository payslips, out IPayslipCalculationTraceRepository traces,
        out IEmployeePayrollProfileRepository profiles, out IPayslipDirtyFlagRepository dirtyFlags,
        out IEmployeeSalaryLookup salaryLookup, out IWorkedTimeReader workedTimeReader, out IExpensePayoutReader expensePayoutReader) =>
        BuildService(out payslips, out traces, out profiles, out dirtyFlags, out salaryLookup, out workedTimeReader, out _, out expensePayoutReader);

    /// <summary>Overload additionally exposing the IAbsenceDaysReader fake (Leave devamsızlık köprüsü, G-7-08/09,
    /// faz-07 tur 2 madde 3): default NSubstitute behaviour (GetUnexcusedDaysAsync returns null) reproduces "no
    /// absence data" without any .Returns() setup, so every other test in this file is unaffected.</summary>
    private static PayrollPeriodCalculationService BuildService(
        out IPayslipRepository payslips, out IPayslipCalculationTraceRepository traces,
        out IEmployeePayrollProfileRepository profiles, out IPayslipDirtyFlagRepository dirtyFlags,
        out IEmployeeSalaryLookup salaryLookup, out IWorkedTimeReader workedTimeReader,
        out IAbsenceDaysReader absenceDaysReader, out IExpensePayoutReader expensePayoutReader) =>
        BuildService(out payslips, out traces, out profiles, out dirtyFlags, out salaryLookup, out workedTimeReader, out absenceDaysReader, out expensePayoutReader, out _);

    /// <summary>Overload additionally exposing the IEmployeeUnionMembershipRepository fake (#58 "Sendika /
    /// dayanışma aidatı"): default NSubstitute behaviour (FindCurrentAsync returns null) reproduces "no union
    /// membership" - UnionDuesCalculator.Calculate then returns 0 - without any .Returns() setup, so every other
    /// test in this file is unaffected.</summary>
    private static PayrollPeriodCalculationService BuildService(
        out IPayslipRepository payslips, out IPayslipCalculationTraceRepository traces,
        out IEmployeePayrollProfileRepository profiles, out IPayslipDirtyFlagRepository dirtyFlags,
        out IEmployeeSalaryLookup salaryLookup, out IWorkedTimeReader workedTimeReader,
        out IAbsenceDaysReader absenceDaysReader, out IExpensePayoutReader expensePayoutReader,
        out IEmployeeUnionMembershipRepository unionMemberships, IPayrollPeriodRepository? periodsOverride = null,
        Sense.Modules.Leave.Contracts.IOvertimePayrollReader? overtimeOverride = null,
        IPayrollAdjustmentRepository? adjustmentsOverride = null)
    {
        payslips = Substitute.For<IPayslipRepository>();
        payslips.FindByEmployeeAndPeriodAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payslip?>(null));
        traces = Substitute.For<IPayslipCalculationTraceRepository>();
        profiles = Substitute.For<IEmployeePayrollProfileRepository>();
        dirtyFlags = Substitute.For<IPayslipDirtyFlagRepository>();
        dirtyFlags.FindActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PayslipDirtyFlag?>(null));
        salaryLookup = Substitute.For<IEmployeeSalaryLookup>();
        workedTimeReader = Substitute.For<IWorkedTimeReader>();
        absenceDaysReader = Substitute.For<IAbsenceDaysReader>();
        expensePayoutReader = Substitute.For<IExpensePayoutReader>();
        unionMemberships = Substitute.For<IEmployeeUnionMembershipRepository>();
        var overtimeReader = overtimeOverride ?? Substitute.For<Sense.Modules.Leave.Contracts.IOvertimePayrollReader>();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        // senseik-019 2a/2b/2c ports: default NSubstitute behaviour (null/empty for any Arg without an explicit
        // .Returns()) reproduces "no prior period, no disability, no garnishment orders" - the pre-existing tests in
        // this file don't exercise these scenarios (covered instead by PayrollCalculatorGoldenSetTests at the pure
        // Domain level), so none of them need to be exposed as an `out` parameter here.
        var periods = periodsOverride ?? Substitute.For<IPayrollPeriodRepository>();
        var garnishmentOrders = Substitute.For<IGarnishmentOrderRepository>();
        var disabilityLookup = Substitute.For<IEmployeeDisabilityLookup>();

        return new PayrollPeriodCalculationService(
            payslips, traces, profiles, dirtyFlags, salaryLookup, Legislation(), tenant, TimeProvider.System,
            workedTimeReader, absenceDaysReader, expensePayoutReader, periods, garnishmentOrders, disabilityLookup, unionMemberships,
            overtimeReader, adjustmentsOverride ?? Substitute.For<IPayrollAdjustmentRepository>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PayrollPeriodCalculationService>.Instance);
    }

    [Fact]
    public async Task RecalculateAllAsync_AddsTheApprovedOvertimePayToTheGross()
    {
        var overtime = Substitute.For<Sense.Modules.Leave.Contracts.IOvertimePayrollReader>();
        var employee = Guid.NewGuid();
        // 10 hours at 25%: hourly 50,000 / 225 = 222.22; 10 * 222.22 * 1.25 = 2,777.75
        overtime.GetAsync(TenantId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sense.Modules.Leave.Contracts.OvertimePayrollLine>>([new Sense.Modules.Leave.Contracts.OvertimePayrollLine(employee, 600, 0, 0)]));
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, overtimeOverride: overtime);
        profiles.FindCurrentAsync(employee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employee)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        Payslip? created = null;
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => created = call.Arg<Payslip>());

        await service.RecalculateAllAsync(OpenPeriod(), [employee], CancellationToken.None);

        created.ShouldNotBeNull();
        created.GrossAmount.ShouldBe(52_777.75m);
    }

    [Fact]
    public async Task RecalculateAllAsync_AddsTheMonthsBonusToTheGross()
    {
        var adjustments = Substitute.For<IPayrollAdjustmentRepository>();
        var employee = Guid.NewGuid();
        adjustments.ListByEmployeeAndMonthAsync(employee, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment>>(
                [Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment.Create(TenantId, employee, 2026, 8, Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustmentKind.Bonus, "Prim", 10_000m).Value]));
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, adjustmentsOverride: adjustments);
        profiles.FindCurrentAsync(employee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employee)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        Payslip? created = null;
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => created = call.Arg<Payslip>());

        await service.RecalculateAllAsync(OpenPeriod(), [employee], CancellationToken.None);

        created.ShouldNotBeNull();
        created.GrossAmount.ShouldBe(60_000m);
    }

    [Theory]
    [InlineData(TransportVariant.Cash, 800, true)]
    [InlineData(TransportVariant.Card, 800, false)]
    [InlineData(TransportVariant.Shuttle, 0, false)]
    public async Task RecalculateAllAsync_CashTransportAddsTaxablePay_ButInKindTransportDoesNot(TransportVariant variant, int dailyAmount, bool paysCash)
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employee = Guid.NewGuid();
        profiles.FindCurrentAsync(employee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(ProfileWithTransport(employee, variant, dailyAmount)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        Payslip? created = null;
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => created = call.Arg<Payslip>());

        await service.RecalculateAllAsync(OpenPeriod(), [employee], CancellationToken.None);

        var period = OpenPeriod();
        var workedDays = DateTime.DaysInMonth(period.Year, period.Month);
        created.ShouldNotBeNull();
        created.GrossAmount.ShouldBe(50_000m + (paysCash ? dailyAmount * workedDays : 0));
    }

    [Fact]
    public async Task PreviewAsync_ReturnsTheFigures_ButStoresNothing()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup);
        var employee = Guid.NewGuid();
        profiles.FindCurrentAsync(employee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employee)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));

        var preview = await service.PreviewAsync(OpenPeriod(), [employee], CancellationToken.None);

        preview.Summary.CalculatedCount.ShouldBe(1);
        var line = preview.Lines.ShouldHaveSingleItem();
        line.EmployeeId.ShouldBe(employee);
        line.Gross.ShouldBe(50_000m);
        line.Net.ShouldBeGreaterThan(0m);
        line.EmployerCost.ShouldBeGreaterThan(line.Gross);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
        traces.DidNotReceive().Add(Arg.Any<PayslipCalculationTrace>());
    }

    [Fact]
    public async Task RecalculateAllAsync_GrossesUpANetBonus_SoTheEmployeeKeepsExactlyThatNet()
    {
        var employee = Guid.NewGuid();
        var without = await CalculateWithBonus(employee, null);
        var netBonus = Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment.Create(
            TenantId, employee, 2026, 3, Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustmentKind.Bonus, "Net prim", 10_000m, isNet: true).Value;

        var with = await CalculateWithBonus(employee, netBonus);

        with.GrossAmount.ShouldBeGreaterThan(without.GrossAmount + 10_000m);
        (with.NetAmount - without.NetAmount).ShouldBe(10_000m, 0.05m);
    }

    private static async Task<Payslip> CalculateWithBonus(Guid employee, Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment? bonus)
    {
        var adjustments = Substitute.For<IPayrollAdjustmentRepository>();
        adjustments.ListByEmployeeAndMonthAsync(employee, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment>>(bonus is null ? [] : [bonus]));
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, adjustmentsOverride: adjustments);
        profiles.FindCurrentAsync(employee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employee)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        Payslip? created = null;
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => created = call.Arg<Payslip>());
        await service.RecalculateAllAsync(OpenPeriod(), [employee], CancellationToken.None);
        return created!;
    }

    [Fact]
    public async Task RecalculateAllAsync_PaysAnExtraDayForEachHolidayAndRestDayWorked()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out var workedTime);
        var employee = Guid.NewGuid();
        profiles.FindCurrentAsync(employee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employee)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        workedTime.GetAsync(TenantId, employee, 2026, 3, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Sense.Modules.Payroll.Application.Ports.WorkedTimeSummary?>(new Sense.Modules.Payroll.Application.Ports.WorkedTimeSummary(31, 0, 0m, PublicHolidayDaysWorked: 1, WeeklyRestDaysWorked: 1)));
        Payslip? created = null;
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => created = call.Arg<Payslip>());

        await service.RecalculateAllAsync(OpenPeriod(), [employee], CancellationToken.None);

        created.ShouldNotBeNull();
        created.GrossAmount.ShouldBe(50_000m + HolidayPayCalculator.Calculate(50_000m, 31, 1, 1));
    }

    [Fact]
    public void ACashTransportProfile_NeedsADailyAmount_AndOtherVariantsKeepZero()
    {
        var cash = EmployeePayrollProfileData_(TransportVariant.Cash, 0m);
        EmployeePayrollProfile.Validate(cash).Error.Code.ShouldBe(PayrollDomainErrors.ProfileTransportAmountInvalid);

        var card = ProfileWithTransport(Guid.NewGuid(), TransportVariant.Card, 500m);
        card.TransportDailyAmount.ShouldBe(0m);
    }

    private static EmployeePayrollProfileData EmployeePayrollProfileData_(TransportVariant variant, decimal dailyAmount) =>
        new(SgkWorkplaceCompanyId, null, new DateOnly(2026, 1, 1), null, WageBasis.Monthly, SgkStatus.Normal, null,
            MealBenefitVariant.None, 0m, variant, PayrollPaymentMethod.Bank, null, false, null, null, dailyAmount);

    [Fact]
    public async Task RecalculateAllAsync_ProducesAPayslipForEveryEmployeeWithAProfileAndSalary()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeA, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeA)));
        profiles.FindCurrentAsync(employeeB, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeB)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeA, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeB, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(80_000m)));

        var summary = await service.RecalculateAllAsync(OpenPeriod(), [employeeA, employeeB], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(2);
        summary.SkippedCount.ShouldBe(0);
        payslips.Received(2).Add(Arg.Any<Payslip>());
        traces.Received(2).Add(Arg.Any<PayslipCalculationTrace>());
    }

    [Fact]
    public async Task RecalculateAllAsync_SkipsAnEmployeeWithNoCurrentPayrollProfile()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(null));

        var summary = await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(0);
        summary.SkippedCount.ShouldBe(1);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
        await salaryLookup.DidNotReceive().GetSalaryAsOfAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateDirtyAsync_OnlyCalculatesEmployeesWithAnActiveDirtyFlag_AndClearsIt()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out var dirtyFlags, out var salaryLookup);
        var dirtyEmployee = Guid.NewGuid();
        var untouchedEmployee = Guid.NewGuid();
        var period = OpenPeriod();

        var activeFlag = PayslipDirtyFlag.Create(TenantId, period.Id, dirtyEmployee, PayslipDirtyFlagReasons.SalaryChanged, Guid.NewGuid(), DateTime.UtcNow);
        dirtyFlags.ListActiveByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PayslipDirtyFlag>>([activeFlag]));
        dirtyFlags.FindActiveAsync(period.Id, dirtyEmployee, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDirtyFlag?>(activeFlag));

        profiles.FindCurrentAsync(dirtyEmployee, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(dirtyEmployee)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, dirtyEmployee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));

        var summary = await service.RecalculateDirtyAsync(period, CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        payslips.Received(1).Add(Arg.Any<Payslip>());

        // The untouched employee is never looked up at all - RecalculateDirtyAsync only iterates flagged ids.
        await profiles.DidNotReceive().FindCurrentAsync(untouchedEmployee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await salaryLookup.DidNotReceive().GetSalaryAsOfAsync(TenantId, untouchedEmployee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

        activeFlag.IsActive.ShouldBeFalse();
        activeFlag.ClearedByRunId.ShouldBe(summary.RunId);
    }

    /// <summary>TB-7 port (tur 3 TALIMAT madde 2): no IWorkedTimeReader data (default NSubstitute behaviour - GetAsync
    /// returns null for any Arg without an explicit .Returns()) must reproduce exactly the pre-port behaviour, the
    /// DAYS trace step's workedDays = the calendar month's day count.</summary>
    /// <summary>Md.3 test 3/3 (faz-07 tur 2 madde 4): neither Attendance nor Leave has data (both ports' default
    /// NSubstitute null) - keeps the pre-existing full-calendar-month assumption.</summary>
    [Fact]
    public async Task RecalculateAllAsync_FallsBackToFullCalendarMonth_WhenWorkedTimeReaderHasNoData()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out _);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedDaysStepOutput(traces).ShouldBe(31); // March 2026
    }

    /// <summary>Md.3 test 1/3 (faz-07 tur 2 madde 4, "çift sayım kuralı"): Attendance (PDKS) has data - the Leave
    /// absence bridge is never even consulted, let alone allowed to reduce WorkedDays further.</summary>
    [Fact]
    public async Task RecalculateAllAsync_IgnoresAbsenceDaysReader_WhenWorkedTimeReaderHasData()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out var workedTimeReader, out var absenceDaysReader, out _);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 3, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new WorkedTimeSummary(WorkedDays: 18, MissingDays: 13, OvertimeHours: 4m)));
        absenceDaysReader.GetUnexcusedDaysAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<int?>(5));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedDaysStepOutput(traces).ShouldBe(18); // PDKS değeri aynen kullanılır, absence'ın 5'i hiç sorulmaz.
        await absenceDaysReader.DidNotReceive().GetUnexcusedDaysAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Md.3 test 2/3 (faz-07 tur 2 madde 4): no Attendance data, but Leave has 5 Unexcused absence days this
    /// month - WorkedDays drops from the full calendar month (31 for March 2026) by that count.</summary>
    [Fact]
    public async Task RecalculateAllAsync_ReducesWorkedDaysByUnexcusedAbsences_WhenWorkedTimeReaderHasNoDataButAbsencesDo()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out _, out var absenceDaysReader, out _);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        absenceDaysReader.GetUnexcusedDaysAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<int?>(5));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedDaysStepOutput(traces).ShouldBe(26); // 31 (Mart 2026) - 5 unexcused
    }

    /// <summary>TB-7 port: a fake with data (WorkedDays=18) overrides the full-month assumption - the DAYS trace step
    /// records the attendance-provided figure instead of CalendarDaysInMonth.</summary>
    [Fact]
    public async Task RecalculateAllAsync_UsesWorkedTimeReaderWorkedDays_WhenDataIsAvailable()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out var workedTimeReader);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 3, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new WorkedTimeSummary(WorkedDays: 18, MissingDays: 13, OvertimeHours: 4m)));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedDaysStepOutput(traces).ShouldBe(18);
    }

    /// <summary>TB-7 Expense köprüsü (faz-07 tur 1 madde 3): no IExpensePayoutReader data (default NSubstitute
    /// behaviour) must reproduce exactly the pre-port behaviour - NET's expenseReimbursement input stays 0.</summary>
    [Fact]
    public async Task RecalculateAllAsync_AddsNoReimbursement_WhenExpensePayoutReaderHasNoData()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out _, out _);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedNetStepExpenseReimbursementInput(traces).ShouldBe("0");
    }

    /// <summary>TB-7 Expense köprüsü: a fake with an approved-this-month reimbursement (2,500) is added straight to
    /// NET's expenseReimbursement input - it is tax-free and never touches SGK/GV/damga matrahları.</summary>
    [Fact]
    public async Task RecalculateAllAsync_AddsApprovedExpenseTotalToNet_WhenExpensePayoutReaderHasData()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out _, out var expensePayoutReader);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        expensePayoutReader.GetAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpensePayoutFigures?>(new ExpensePayoutFigures(ApprovedExpenseTotal: 2_500m, OpenAdvanceBalance: 1_000m)));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedNetStepExpenseReimbursementInput(traces).ShouldBe("2500");
    }

    /// <summary>S-13 eki (faz-07 tur 2 madde 5, PO kararı): UndocumentedExpenseTotal (fişsiz kalemler) is Excel-only
    /// information, never fed into NET - only ApprovedExpenseTotal (2,500 here) reaches the trace, not the 900 fişsiz
    /// figure alongside it.</summary>
    [Fact]
    public async Task RecalculateAllAsync_IgnoresUndocumentedExpenseTotal_ForNetReimbursement()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out _, out var expensePayoutReader);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        expensePayoutReader.GetAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpensePayoutFigures?>(new ExpensePayoutFigures(ApprovedExpenseTotal: 2_500m, OpenAdvanceBalance: 0m, UndocumentedExpenseTotal: 900m)));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedNetStepExpenseReimbursementInput(traces).ShouldBe("2500");
    }

    // ---- QA teslim testi düzeltmeleri: kümülatif GV, kıst maaş, dönem-tarihli maaş, atlanan çalışan nedenleri ----

    private static PayrollPeriod SeptemberPeriod() => PayrollPeriod.Create(TenantId, 2026, 9).Value;

    private static void GivenEmployee(
        IEmployeePayrollProfileRepository profiles, IEmployeeSalaryLookup salaryLookup, Guid employeeId,
        EmployeeCurrentSalary? salary = null, DateOnly? hireDate = null, DateOnly? terminationDate = null, DateOnly? profileAsOf = null)
    {
        profiles.FindCurrentAsync(employeeId, profileAsOf ?? Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeCurrentSalary?>(salary ?? Salary()));
        salaryLookup.GetEmploymentDatesAsync(TenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeEmploymentDates?>(new EmployeeEmploymentDates(hireDate, terminationDate)));
    }

    [Fact]
    public async Task RecalculateAllAsync_CarriesPriorMonthCumulativeGvBase_IntoTheIncomeTaxStep()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var august = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        periods.FindByYearMonthAsync(2026, 8, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(august));
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, periods);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId, Salary(75_000m));
        payslips.FindByEmployeeAndPeriodAsync(employeeId, august.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payslip?>(Payslip.CreateFirstVersion(TenantId, employeeId, august.Id, "{}", "hash", 75_000m, 57_670.95m, 90_000m, cumulativeGvBase: 500_000m)));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        var steps = CapturedSteps(traces);
        steps.Single(s => s.Code == PayrollTraceStepCodes.IncomeTax).Inputs["priorCumulativeGvBase"].ShouldBe("500000");
        steps.Single(s => s.Code == PayrollTraceStepCodes.CumulativeBase).Output.ShouldBeGreaterThan(500_000m);
    }

    [Fact]
    public async Task RecalculateAllAsync_FallsBackToTheLatestEarlierPayslipOfTheYear_WhenThePreviousMonthHasNone()
    {
        var periods = Substitute.For<IPayrollPeriodRepository>();
        var july = PayrollPeriod.Create(TenantId, 2026, 7).Value;
        periods.FindByYearMonthAsync(2026, 7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(july));
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, periods);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId);
        payslips.FindByEmployeeAndPeriodAsync(employeeId, july.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payslip?>(Payslip.CreateFirstVersion(TenantId, employeeId, july.Id, "{}", "hash", 50_000m, 40_000m, 60_000m, cumulativeGvBase: 300_000m)));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.IncomeTax).Inputs["priorCumulativeGvBase"].ShouldBe("300000");
    }

    [Fact]
    public async Task RecalculateAllAsync_StartsCumulativeBaseAtZero_WhenTheEmployeeHasNoEarlierPayslipThisYear()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId);

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.IncomeTax).Inputs["priorCumulativeGvBase"].ShouldBe("0");
    }

    [Fact]
    public async Task RecalculateAllAsync_ProratesSalaryAndDays_ForAMidMonthHire()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        var hireDate = new DateOnly(2026, 9, 16);
        // The hire-date profile is only valid from the hire date, so it must be looked up as of that date, not Sep 1.
        GivenEmployee(profiles, salaryLookup, employeeId, Salary(50_000m), hireDate: hireDate, profileAsOf: hireDate);

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        var steps = CapturedSteps(traces);
        steps.Single(s => s.Code == PayrollTraceStepCodes.Days).Output.ShouldBe(15m); // 16..30 => 15 days
        steps.Single(s => s.Code == PayrollTraceStepCodes.Earnings).Output.ShouldBe(25_000m); // 50,000 * 15 / 30
    }

    [Fact]
    public async Task RecalculateAllAsync_ProratesSalaryAndDays_ForAMidMonthTermination()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId, Salary(60_000m), terminationDate: new DateOnly(2026, 9, 10));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        var steps = CapturedSteps(traces);
        steps.Single(s => s.Code == PayrollTraceStepCodes.Days).Output.ShouldBe(10m);
        steps.Single(s => s.Code == PayrollTraceStepCodes.Earnings).Output.ShouldBe(20_000m); // 60,000 * 10 / 30
    }

    [Fact]
    public async Task RecalculateAllAsync_SkipsAnEmployeeHiredAfterThePeriod_WithAReason()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId, hireDate: new DateOnly(2026, 10, 1));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(0);
        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(employeeId, PayrollSkipReasons.NotEmployedInPeriod)]);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }

    [Fact]
    public async Task RecalculateAllAsync_SkipsAnEmployeeTerminatedBeforeThePeriod_WithAReason()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId, terminationDate: new DateOnly(2026, 8, 31));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.Skipped.Single().Reason.ShouldBe(PayrollSkipReasons.NotEmployedInPeriod);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }

    [Fact]
    public async Task RecalculateAllAsync_ReadsTheSalaryInForceAtPeriodEnd_NotTheCurrentOne()
    {
        var service = BuildService(out _, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, new DateOnly(2026, 8, 31), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(75_000m)));

        var augustPeriod = PayrollPeriod.Create(TenantId, 2026, 8).Value;
        var summary = await service.RecalculateAllAsync(augustPeriod, [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        await salaryLookup.Received(1).GetSalaryAsOfAsync(TenantId, employeeId, new DateOnly(2026, 8, 31), Arg.Any<CancellationToken>());
        await salaryLookup.DidNotReceive().GetCurrentSalaryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateAllAsync_GrossesUpANetSalary_SoTheEmployeeKeepsThatNet()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, employeeId,
            new EmployeeCurrentSalary(40_000m, "TRY", EmployeeSalaryType.Net, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1)));

        Payslip? created = null;
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => created = call.Arg<Payslip>());

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        summary.SkippedCount.ShouldBe(0);
        created.ShouldNotBeNull();
        created.GrossAmount.ShouldBeGreaterThan(40_000m);
        Math.Abs(created.NetAmount - 40_000m).ShouldBeLessThan(0.05m); // no meal, expenses or deductions in this profile
    }

    [Fact]
    public async Task RecalculateAllAsync_SkipsAnEmployeeWithZeroWorkedDays_AndStillCalculatesTheOthers()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out var workedTimeReader);
        var noDays = Guid.NewGuid();
        var normal = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, noDays);
        GivenEmployee(profiles, salaryLookup, normal);
        workedTimeReader.GetAsync(TenantId, noDays, 2026, 9, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new WorkedTimeSummary(WorkedDays: 0, MissingDays: 30, OvertimeHours: 0m)));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [noDays, normal], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(noDays, PayrollSkipReasons.NoWorkedDays)]);
        payslips.Received(1).Add(Arg.Any<Payslip>());
    }

    [Fact]
    public async Task RecalculateAllAsync_OneEmployeeFailingTheCalculation_DoesNotBreakTheWholeRun()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var broken = Guid.NewGuid();
        var normal = Guid.NewGuid();
        GivenEmployee(profiles, salaryLookup, broken, Salary(0m)); // PayrollCalculator rejects a non-positive gross
        GivenEmployee(profiles, salaryLookup, normal);

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [broken, normal], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(broken, PayrollSkipReasons.CalculationFailed)]);
        payslips.Received(1).Add(Arg.Any<Payslip>());
    }

    // ---- Günlük / saatlik ücret (PAY-WAGE-BASIS) ----

    private static EmployeePayrollProfile ProfileWithWageBasis(Guid employeeId, WageBasis wageBasis) => EmployeePayrollProfile.Create(
        TenantId, employeeId,
        new EmployeePayrollProfileData(
            SgkWorkplaceCompanyId, null, new DateOnly(2026, 1, 1), null,
            wageBasis, SgkStatus.Normal, null,
            MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null,
            SickDaysReducePay: false, ExternalPayrollCode: null, Notes: null)).Value;

    private static EmployeeCurrentSalary WageSalary(EmployeePayFrequency frequency, decimal amount, EmployeeSalaryType type = EmployeeSalaryType.Gross) =>
        new(amount, "TRY", type, frequency, new DateOnly(2026, 1, 1));

    private static void GivenWageEmployee(
        IEmployeePayrollProfileRepository profiles, IEmployeeSalaryLookup salaryLookup, Guid employeeId,
        WageBasis basis, EmployeeCurrentSalary salary, DateOnly? hireDate = null)
    {
        profiles.FindCurrentAsync(employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeePayrollProfile?>(ProfileWithWageBasis(employeeId, basis)));
        salaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeCurrentSalary?>(salary));
        salaryLookup.GetEmploymentDatesAsync(TenantId, employeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeEmploymentDates?>(new EmployeeEmploymentDates(hireDate, null)));
    }

    private static decimal CapturedGross(IPayslipRepository payslips)
    {
        var created = (Payslip)payslips.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IPayslipRepository.Add)).GetArguments()[0]!;
        return created.GrossAmount;
    }

    [Fact]
    public async Task RecalculateAllAsync_CalculatesAFullMonthDailyWage_AsDailyRateTimes30PaidDays()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        CapturedGross(payslips).ShouldBe(60_000m); // 2,000 x 30
        var earnings = CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.Earnings);
        earnings.Inputs["wageBasis"].ShouldBe("daily");
        earnings.Inputs["unitRate"].ShouldBe("2000");
        earnings.Inputs["paidDays"].ShouldBe("30");
    }

    [Fact]
    public async Task RecalculateAllAsync_CalculatesAFullMonthHourlyWage_AsHourlyRateTimes7Point5TimesPaidDays()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Hourly, WageSalary(EmployeePayFrequency.Hourly, 200m));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        CapturedGross(payslips).ShouldBe(45_000m); // 200 x 7.5 x 30 (225 saat)
        var earnings = CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.Earnings);
        earnings.Inputs["wageBasis"].ShouldBe("hourly");
        earnings.Inputs["paidHours"].ShouldBe("225");
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWageHiredMidMonth_IsNotProratedTwice_ButSgkBoundsFollowThePaidDays()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m), hireDate: new DateOnly(2026, 9, 16));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(30_000m); // 2,000 x 15, not 60,000 x 15/30 on top of that
        var sgkBase = CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.SgkBase);
        sgkBase.Inputs["sgkCeiling"].ShouldBe("112500"); // 225,000 x 15/30
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWage_DeductsUnexcusedAbsenceDaysFromThePaidDays()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out var absenceDaysReader, out _);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));
        absenceDaysReader.GetUnexcusedDaysAsync(TenantId, employeeId, 2026, 9, Arg.Any<CancellationToken>()).Returns(Task.FromResult<int?>(5));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(50_000m); // 2,000 x (30 - 5)
    }

    [Fact]
    public async Task RecalculateAllAsync_HourlyWage_UsesAttendanceMissingDaysToReduceThePaidDays()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out var workedTimeReader);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Hourly, WageSalary(EmployeePayFrequency.Hourly, 200m));
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 9, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new WorkedTimeSummary(WorkedDays: 26, MissingDays: 4, OvertimeHours: 0m)));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(39_000m); // 200 x 7.5 x (30 - 4)
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWage_WithEveryDayMissing_IsSkippedAsNoWorkedDays()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out var absenceDaysReader, out _);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));
        absenceDaysReader.GetUnexcusedDaysAsync(TenantId, employeeId, 2026, 9, Arg.Any<CancellationToken>()).Returns(Task.FromResult<int?>(30));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(employeeId, PayrollSkipReasons.NoWorkedDays)]);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }

    [Theory]
    [InlineData(WageBasis.Weekly, EmployeePayFrequency.Weekly)]
    [InlineData(WageBasis.Yearly, EmployeePayFrequency.Yearly)]
    public async Task RecalculateAllAsync_StillSkipsWeeklyAndYearlyWageBasis(WageBasis basis, EmployeePayFrequency frequency)
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, basis, WageSalary(frequency, 10_000m));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(employeeId, PayrollSkipReasons.WageBasisNotMonthly)]);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }

    [Theory]
    [InlineData(WageBasis.Daily, EmployeePayFrequency.Monthly)]
    [InlineData(WageBasis.Monthly, EmployeePayFrequency.Hourly)]
    [InlineData(WageBasis.Hourly, EmployeePayFrequency.Daily)]
    public async Task RecalculateAllAsync_SkipsAnEmployeeWhoseWageBasisDisagreesWithThePayFrequency(WageBasis basis, EmployeePayFrequency frequency)
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, basis, WageSalary(frequency, 2_000m));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(employeeId, PayrollSkipReasons.WageBasisMismatch)]);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }

    [Fact]
    public async Task RecalculateAllAsync_SkipsANetDailyWage_WithAnExplicitReason()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 1_500m, EmployeeSalaryType.Net));

        var summary = await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        summary.Skipped.ShouldBe([new PayrollSkippedEmployee(employeeId, PayrollSkipReasons.NetWageBasisUnsupported)]);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWage_PaysOvertimeOnTheMonthlyEquivalent()
    {
        var overtime = Substitute.For<Sense.Modules.Leave.Contracts.IOvertimePayrollReader>();
        var employeeId = Guid.NewGuid();
        // 10 hours at 25%: monthly equivalent 2,000 x 30 = 60,000; hourly 60,000 / 225 = 266.67; 10 x 266.67 x 1.25 = 3,333.38
        overtime.GetAsync(TenantId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sense.Modules.Leave.Contracts.OvertimePayrollLine>>([new Sense.Modules.Leave.Contracts.OvertimePayrollLine(employeeId, 600, 0, 0)]));
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, overtimeOverride: overtime);
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(63_333.38m);
    }

    [Fact]
    public void PeriodWageMath_UsesA225HourMonth_AndA30DayMonth()
    {
        PeriodWageMath.MonthlyWorkingHours.ShouldBe(225m);
        PeriodWageMath.MonthlyEquivalent(WageBasis.Daily, 100m).ShouldBe(3_000m);
        PeriodWageMath.MonthlyEquivalent(WageBasis.Hourly, 100m).ShouldBe(22_500m);
        PeriodWageMath.PeriodGross(WageBasis.Hourly, 100m, 10).ShouldBe(7_500m);
        Should.Throw<NotSupportedException>(() => PeriodWageMath.PeriodGross(WageBasis.Weekly, 100m, 10));
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWageHiredMidMonth_PassesTheEmploymentWindowToAttendance_SoDaysBeforeHireAreNotDeductedTwice()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out var workedTimeReader);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m), hireDate: new DateOnly(2026, 9, 16));
        // Attendance sees only 16-30 Eylül: 2 eksik iş günü pencere içinde.
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 9, new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 30), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new WorkedTimeSummary(WorkedDays: 11, MissingDays: 2, OvertimeHours: 0m)));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(26_000m); // 2,000 x (15 - 2)
    }

    [Fact]
    public async Task RecalculateAllAsync_FullMonthWorker_DoesNotPassAnAttendanceWindow()
    {
        var service = BuildService(out _, out _, out var profiles, out _, out var salaryLookup, out var workedTimeReader);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        await workedTimeReader.Received(1).GetAsync(TenantId, employeeId, 2026, 9, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWageHiredMidMonth_DeductsUnexcusedAbsenceFromTheKistDays()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out var absenceDaysReader, out _);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m), hireDate: new DateOnly(2026, 9, 16));
        absenceDaysReader.GetUnexcusedDaysAsync(TenantId, employeeId, 2026, 9, Arg.Any<CancellationToken>()).Returns(Task.FromResult<int?>(2));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(26_000m); // 2,000 x (15 - 2)
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWageHiredMidMonth_AddsABonusOnTopOfTheKistWage()
    {
        var adjustments = Substitute.For<IPayrollAdjustmentRepository>();
        var employeeId = Guid.NewGuid();
        adjustments.ListByEmployeeAndMonthAsync(employeeId, 2026, 9, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment>>(
                [Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustment.Create(TenantId, employeeId, 2026, 9, Sense.Modules.Payroll.Domain.Adjustments.PayrollAdjustmentKind.Bonus, "Prim", 10_000m).Value]));
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _, adjustmentsOverride: adjustments);
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m), hireDate: new DateOnly(2026, 9, 16));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(40_000m); // 2,000 x 15 + 10,000, the bonus is not prorated
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWage_PaysHolidayWorkOnTheDailyRate()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaryLookup, out var workedTimeReader);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 9, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new WorkedTimeSummary(30, 0, 0m, PublicHolidayDaysWorked: 1, WeeklyRestDaysWorked: 1)));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedGross(payslips).ShouldBe(64_000m); // 60,000 + 2 x (60,000 / 30)
    }

    [Fact]
    public async Task RecalculateAllAsync_DailyWage_ComputesUnionDuesOnTheMonthlyEquivalent()
    {
        var service = BuildService(out _, out var traces, out var profiles, out _, out var salaryLookup, out _, out _, out _, out var unionMemberships);
        var employeeId = Guid.NewGuid();
        GivenWageEmployee(profiles, salaryLookup, employeeId, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));
        var membership = EmployeeUnionMembership.Create(TenantId, employeeId, new EmployeeUnionMembershipData(
            Guid.NewGuid(), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 15), UnionCoverageType.Member, UnionDuesRule.PercentOfGross, 0.01m, null)).Value;
        unionMemberships.FindCurrentAsync(employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeUnionMembership?>(membership));

        await service.RecalculateAllAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.Deductions).Inputs["unionDues"].ShouldBe("600"); // 1% x (2,000 x 30)
    }

    private static decimal CapturedDaysStepOutput(IPayslipCalculationTraceRepository traces)
    {
        var steps = CapturedSteps(traces);
        return steps.Single(s => s.Code == PayrollTraceStepCodes.Days).Output;
    }

    private static string CapturedNetStepExpenseReimbursementInput(IPayslipCalculationTraceRepository traces)
    {
        var steps = CapturedSteps(traces);
        return steps.Single(s => s.Code == PayrollTraceStepCodes.Net).Inputs["expenseReimbursement"];
    }

    private static List<TraceStep> CapturedSteps(IPayslipCalculationTraceRepository traces)
    {
        traces.Received(1).Add(Arg.Any<PayslipCalculationTrace>());
        var captured = (PayslipCalculationTrace)traces.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPayslipCalculationTraceRepository.Add))
            .GetArguments()[0]!;
        return JsonSerializer.Deserialize<List<TraceStep>>(captured.StepsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
