using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Shouldly;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>Shared test data for the incentive calculation tests: the real 2026 legislation values and the recorded-fixture helper.</summary>
internal static class IncentiveTestKit
{
    public static readonly DateOnly AsOf = new(2026, 3, 1);

    /// <summary>The real seeded 2026 values (LegislationParameterSeed ids 25-36, 39, 43-45): asgari ücret 33.030, SGK tavanı 247.725, dilimler 190.000/400.000/1.500.000/5.300.000.</summary>
    public static readonly PayrollLegislationSnapshot Snapshot = new(
        [new TaxBracket(190_000m, 0.15m), new TaxBracket(400_000m, 0.20m), new TaxBracket(1_500_000m, 0.27m), new TaxBracket(5_300_000m, 0.35m), new TaxBracket(null, 0.40m)],
        SgkFloor: 33_030m, SgkCeiling: 247_725m, SgkEmployeeRate: 0.14m, UnemploymentEmployeeRate: 0.01m, SgkEmployerRate: 0.205m,
        UnemploymentEmployerRate: 0.02m, SgkEmployerDiscountRate: 0.05m, StampTaxRate: 0.00759m, MinimumWageGross: 33_030m,
        MealIncomeTaxDailyExemption: 240m, MealSgkDailyExemptionRate: 0.2365m, MealMarketCardExemptionApplies: false,
        DisabilityMonthlyTaxReductionDegree1: 6900m, DisabilityMonthlyTaxReductionDegree2: 4000m, DisabilityMonthlyTaxReductionDegree3: 1700m, AsOf: AsOf);

    public static readonly JsonSerializerOptions StepsOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static PayrollCalculationInput Plain(decimal salary = 200_000m) =>
        new(salary, 30, 30, MealBenefitVariant.None, 0m, 0m, AsOf);

    /// <summary>The recorded fixture: amounts plus the trace steps, as indented JSON.</summary>
    public static string Describe(PayrollCalculationResult result) =>
        Describe(result.GrossAmount, result.NetAmount, result.EmployerCost, JsonSerializer.SerializeToElement(result.Steps, StepsOptions));

    public static string Describe(decimal gross, decimal net, decimal employerCost, JsonElement steps) =>
        JsonSerializer.Serialize(new { grossAmount = gross, netAmount = net, employerCost, steps }, Indented);

    /// <summary>
    /// Compares the actual JSON against the recorded embedded fixture <c>Incentives/Fixtures/{name}.json</c>. A missing fixture fails;
    /// set <c>SENSE_RECORD_FIXTURES=1</c> to (re)write fixtures into the source tree (never done by the test gate).
    /// </summary>
    public static void MatchesFixture(string name, string actual, [CallerFilePath] string callerFile = "")
    {
        if (Environment.GetEnvironmentVariable("SENSE_RECORD_FIXTURES") == "1")
        {
            var dir = Path.Combine(Path.GetDirectoryName(callerFile)!, "Fixtures");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".json"), actual.Replace("\r\n", "\n") + "\n");
            return;
        }

        var assembly = typeof(IncentiveTestKit).Assembly;
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + name + ".json", StringComparison.Ordinal));
        resource.ShouldNotBeNull($"fixture '{name}' is not recorded");
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var expected = reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd();
        actual.Replace("\r\n", "\n").TrimEnd().ShouldBe(expected, $"trace of '{name}' differs from the recorded fixture");
    }
}

/// <summary>
/// A <see cref="PayrollPeriodCalculationService"/> over in-memory/NSubstitute fakes with the real 2026 legislation values, for the
/// incentive tests. Captures the payslips and traces it writes.
/// </summary>
internal sealed class IncentiveServiceHarness
{
    public static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkplaceCompanyId = Guid.NewGuid();

    private readonly Dictionary<Guid, Payslip> _payslips = [];
    private readonly Dictionary<Guid, PayslipCalculationTrace> _traces = [];

    private IncentiveServiceHarness()
    {
    }

    public PayrollPeriodCalculationService Service { get; private set; } = null!;

    public IEmployeePayrollProfileRepository Profiles { get; } = Substitute.For<IEmployeePayrollProfileRepository>();

    public IEmployeeSalaryLookup SalaryLookup { get; } = Substitute.For<IEmployeeSalaryLookup>();

    public FakeIncentiveDefinitionRepository Definitions { get; } = new();

    public FakeIncentiveAssignmentRepository Assignments { get; } = new();

    public Dictionary<string, LegislationParameterValue> Legislation { get; } = [];

    public ILegislationParameterReader LegislationReader { get; } = Substitute.For<ILegislationParameterReader>();

    public PayrollPeriod Period { get; } = PayrollPeriod.Create(TenantId, 2026, 3).Value;

    public static IncentiveServiceHarness Create()
    {
        var harness = new IncentiveServiceHarness();
        harness.Fill();
        return harness;
    }

    public static EmployeePayrollProfile Profile(Guid employeeId, string? incentiveLawCode = null, WageBasis wageBasis = WageBasis.Monthly) => EmployeePayrollProfile.Create(
        TenantId, employeeId,
        new EmployeePayrollProfileData(
            WorkplaceCompanyId, null, new DateOnly(2026, 1, 1), null,
            wageBasis, SgkStatus.Normal, incentiveLawCode,
            MealBenefitVariant.None, 0m, TransportVariant.None, PayrollPaymentMethod.Bank, null,
            SickDaysReducePay: false, ExternalPayrollCode: null, Notes: null)).Value;

    /// <summary>Registers an employee with a profile and a salary.</summary>
    public Guid AddEmployee(string? incentiveLawCode = null, decimal salary = 50_000m, EmployeeSalaryType type = EmployeeSalaryType.Gross)
    {
        var employeeId = Guid.NewGuid();
        Profiles.FindCurrentAsync(employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeePayrollProfile?>(Profile(employeeId, incentiveLawCode)));
        SalaryLookup.GetSalaryAsOfAsync(TenantId, employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeCurrentSalary?>(new EmployeeCurrentSalary(salary, "TRY", type, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1))));
        return employeeId;
    }

    public async Task<PayrollPeriodRunSummary> RunAsync(params Guid[] employeeIds) =>
        await Service.RecalculateAllAsync(Period, employeeIds, CancellationToken.None);

    public async Task<PayrollPreviewResult> PreviewAsync(params Guid[] employeeIds) =>
        await Service.PreviewAsync(Period, employeeIds, CancellationToken.None);

    public Payslip PayslipOf(Guid employeeId) => _payslips[employeeId];

    public PayslipCalculationTrace TraceOf(Guid employeeId) => _traces[_payslips[employeeId].Id];

    public void SetLegislation(string key, decimal value) =>
        Legislation[key] = new LegislationParameterValue(key, LegislationCountries.Turkey, value, null, new DateOnly(2026, 1, 1), null, "test");

    public string StepsJsonOf(Guid employeeId) => _traces[_payslips[employeeId].Id].StepsJson;

    public IReadOnlyList<JsonElement> StepsOf(Guid employeeId) =>
        JsonDocument.Parse(StepsJsonOf(employeeId)).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

    public JsonElement StepOf(Guid employeeId, string code) => StepsOf(employeeId).Single(s => s.GetProperty("code").GetString() == code);

    public decimal OutputOf(Guid employeeId, string code) => StepOf(employeeId, code).GetProperty("output").GetDecimal();

    public bool HasStep(Guid employeeId, string code) => StepsOf(employeeId).Any(s => s.GetProperty("code").GetString() == code);

    /// <summary>The recorded fixture text of the employee's stored payslip and trace.</summary>
    public string DescribeStored(Guid employeeId)
    {
        var payslip = PayslipOf(employeeId);
        return IncentiveTestKit.Describe(payslip.GrossAmount, payslip.NetAmount, payslip.EmployerCost, JsonDocument.Parse(StepsJsonOf(employeeId)).RootElement.Clone());
    }

    private void Fill()
    {
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.FindByEmployeeAndPeriodAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Payslip?>(null));
        payslips.When(p => p.Add(Arg.Any<Payslip>())).Do(call => _payslips[call.Arg<Payslip>().EmployeeId] = call.Arg<Payslip>());
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        traces.When(t => t.Add(Arg.Any<PayslipCalculationTrace>())).Do(call => _traces[call.Arg<PayslipCalculationTrace>().PayslipId] = call.Arg<PayslipCalculationTrace>());
        var dirtyFlags = Substitute.For<IPayslipDirtyFlagRepository>();
        dirtyFlags.FindActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayslipDirtyFlag?>(null));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        SeedLegislation();
        var reader = LegislationReader;
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Legislation.GetValueOrDefault((string)call[0])));

        Service = new PayrollPeriodCalculationService(
            payslips, traces, Profiles, dirtyFlags, SalaryLookup, reader, tenant, TimeProvider.System,
            Substitute.For<IWorkedTimeReader>(), Substitute.For<IAbsenceDaysReader>(), Substitute.For<IExpensePayoutReader>(),
            Substitute.For<IPayrollPeriodRepository>(), Substitute.For<IGarnishmentOrderRepository>(), Substitute.For<IEmployeeDisabilityLookup>(),
            Substitute.For<IEmployeeUnionMembershipRepository>(), Substitute.For<Sense.Modules.Leave.Contracts.IOvertimePayrollReader>(),
            Substitute.For<IPayrollAdjustmentRepository>(), Substitute.For<IEmployeePayItemAssignmentRepository>(), Substitute.For<IPayItemDefinitionRepository>(),
            Assignments, Definitions,
            NullLogger<PayrollPeriodCalculationService>.Instance);
    }

    private void SeedLegislation()
    {
        var s = IncentiveTestKit.Snapshot;
        void Add(string key, decimal value) =>
            Legislation[key] = new LegislationParameterValue(key, LegislationCountries.Turkey, value, null, new DateOnly(2026, 1, 1), null, "test");

        Legislation[LegislationParameterKeys.PayrollIncomeTaxBrackets] = new LegislationParameterValue(
            LegislationParameterKeys.PayrollIncomeTaxBrackets, LegislationCountries.Turkey, null,
            """[{"upperBound":190000,"rate":0.15},{"upperBound":400000,"rate":0.20},{"upperBound":1500000,"rate":0.27},{"upperBound":5300000,"rate":0.35},{"upperBound":null,"rate":0.40}]""",
            new DateOnly(2026, 1, 1), null, "test");
        Add(LegislationParameterKeys.PayrollSgkFloor, s.SgkFloor);
        Add(LegislationParameterKeys.PayrollSgkCeiling, s.SgkCeiling);
        Add(LegislationParameterKeys.PayrollSgkEmployeeRate, s.SgkEmployeeRate);
        Add(LegislationParameterKeys.PayrollUnemploymentEmployeeRate, s.UnemploymentEmployeeRate);
        Add(LegislationParameterKeys.PayrollSgkEmployerRate, s.SgkEmployerRate);
        Add(LegislationParameterKeys.PayrollUnemploymentEmployerRate, s.UnemploymentEmployerRate);
        Add(LegislationParameterKeys.PayrollSgkEmployerDiscountRate, s.SgkEmployerDiscountRate);
        Add(LegislationParameterKeys.PayrollStampTaxRate, s.StampTaxRate);
        Add(LegislationParameterKeys.PayrollMinimumWageGross, s.MinimumWageGross);
        Add(LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption, s.MealIncomeTaxDailyExemption);
        Add(LegislationParameterKeys.PayrollMealSgkDailyExemptionRate, s.MealSgkDailyExemptionRate);
        Add(LegislationParameterKeys.PayrollMealMarketCardExemptionApplies, 0m);
        Add(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree1, s.DisabilityMonthlyTaxReductionDegree1);
        Add(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree2, s.DisabilityMonthlyTaxReductionDegree2);
        Add(LegislationParameterKeys.PayrollDisabilityMonthlyTaxReductionDegree3, s.DisabilityMonthlyTaxReductionDegree3);
    }
}
