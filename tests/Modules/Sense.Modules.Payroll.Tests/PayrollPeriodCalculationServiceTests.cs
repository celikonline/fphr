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
        out IAbsenceDaysReader absenceDaysReader, out IExpensePayoutReader expensePayoutReader)
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

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        return new PayrollPeriodCalculationService(
            payslips, traces, profiles, dirtyFlags, salaryLookup, Legislation(), tenant, TimeProvider.System,
            workedTimeReader, absenceDaysReader, expensePayoutReader);
    }

    [Fact]
    public async Task RecalculateAllAsync_ProducesAPayslipForEveryEmployeeWithAProfileAndSalary()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaryLookup);
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        profiles.FindCurrentAsync(employeeA, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeA)));
        profiles.FindCurrentAsync(employeeB, AsOf, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeB)));
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeA, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(50_000m)));
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeB, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary(80_000m)));

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
        await salaryLookup.DidNotReceive().GetCurrentSalaryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
        salaryLookup.GetCurrentSalaryAsync(TenantId, dirtyEmployee, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));

        var summary = await service.RecalculateDirtyAsync(period, CancellationToken.None);

        summary.CalculatedCount.ShouldBe(1);
        payslips.Received(1).Add(Arg.Any<Payslip>());

        // The untouched employee is never looked up at all - RecalculateDirtyAsync only iterates flagged ids.
        await profiles.DidNotReceive().FindCurrentAsync(untouchedEmployee, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await salaryLookup.DidNotReceive().GetCurrentSalaryAsync(TenantId, untouchedEmployee, Arg.Any<CancellationToken>());

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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));

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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        workedTimeReader.GetAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));

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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
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
        salaryLookup.GetCurrentSalaryAsync(TenantId, employeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeCurrentSalary?>(Salary()));
        expensePayoutReader.GetAsync(TenantId, employeeId, 2026, 3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpensePayoutFigures?>(new ExpensePayoutFigures(ApprovedExpenseTotal: 2_500m, OpenAdvanceBalance: 0m, UndocumentedExpenseTotal: 900m)));

        await service.RecalculateAllAsync(OpenPeriod(), [employeeId], CancellationToken.None);

        CapturedNetStepExpenseReimbursementInput(traces).ShouldBe("2500");
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
        PayslipCalculationTrace? captured = null;
        traces.Received(1).Add(Arg.Do<PayslipCalculationTrace>(t => captured = t));
        return JsonSerializer.Deserialize<List<TraceStep>>(captured!.StepsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
