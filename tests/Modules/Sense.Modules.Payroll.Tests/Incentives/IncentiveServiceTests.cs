using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain.Incentives;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>
/// PAY-INCENTIVE-3: the payroll run with incentive assignments - which models reach the engine, which warnings and skips the run
/// reports, and that the employee's net never moves. Real 2026 legislation values (brüt 50.000: SGK matrahı 50.000, 5 puan = 2.500).
/// </summary>
public sealed class IncentiveServiceTests
{
    private static readonly DateOnly March = new(2026, 3, 1);
    private static readonly Guid TenantId = IncentiveServiceHarness.TenantId;
    private const string DiscountKey = "Payroll.Sgk.EmployerDiscountRate";

    private static IncentiveParameters RateParameters(decimal literal) => new() { Rate = IncentiveRate.Literal(literal) };

    private static IncentiveParameters KeyParameters(string key) => new() { Rate = IncentiveRate.Key(key) };

    private static IncentiveParameters CoverageParameters() => new()
    {
        Rate = IncentiveRate.Literal(1m),
        CoverageBase = IncentiveCoverageBase.ActualSgkBase,
        EmployerSgkRate = IncentiveRate.Literal(0.155m),
        CoversEmployerSgk = true,
    };

    private static IncentiveDefinition Define(
        IncentiveServiceHarness harness, string code, IncentiveKind kind, IncentiveParameters parameters, string? declarationCode, bool approved = true,
        DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        var data = new IncentiveDefinitionData(code + " adı", "Test", declarationCode, kind, parameters, null, validFrom ?? new DateOnly(2026, 1, 1), validTo);
        var definition = IncentiveDefinition.Create(TenantId, code, data).Value;
        if (approved)
        {
            definition.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow, null).IsSuccess.ShouldBeTrue();
        }

        harness.Definitions.Add(definition);
        return definition;
    }

    /// <summary>Assigns without the overlap check, so a test can also build the (concurrent-request) overlap the run must tolerate.</summary>
    private static EmployeeIncentiveAssignment Assign(IncentiveServiceHarness harness, IncentiveDefinition definition, Guid employeeId, DateOnly? from = null)
    {
        var assignment = EmployeeIncentiveAssignment.Create(
            TenantId, employeeId, definition, from ?? March, null, null, new HashSet<DateOnly>(), [], _ => definition.Category).Value;
        harness.Assignments.Add(assignment);
        return assignment;
    }

    /// <summary>The run also reports unrelated warnings (a March run without a February payslip); only the incentive ones matter here.</summary>
    private static IEnumerable<PayrollWarning> IncentiveWarnings(PayrollPeriodRunSummary summary) => summary.Warnings.Where(w => w.Code.StartsWith("incentive_", StringComparison.Ordinal));

    private static IEnumerable<PayrollWarning> WarningsOf(PayrollPeriodRunSummary summary, string code) => summary.Warnings.Where(w => w.Code == code);

    // ---- (a) ----

    [Fact]
    public async Task AnApprovedRateDiscountAssignment_GivesTheSameCostAsTheLegacyProfileCode_WithTheIncentiveNamedInTheTrace()
    {
        var harness = IncentiveServiceHarness.Create();
        var assigned = harness.AddEmployee();
        var legacy = harness.AddEmployee("05510");
        Assign(harness, Define(harness, "SGK_5510_MD81", IncentiveKind.EmployerSgkRateDiscount, KeyParameters(DiscountKey), "05510"), assigned);

        var summary = await harness.RunAsync(assigned, legacy);

        summary.CalculatedCount.ShouldBe(2);
        IncentiveWarnings(summary).ShouldBeEmpty(); // the profile code (same declaration code) is not "ignored"
        harness.OutputOf(assigned, "EMPLOYER_DISCOUNT").ShouldBe(2_500m); // 50.000 x 0,05
        harness.OutputOf(legacy, "EMPLOYER_DISCOUNT").ShouldBe(2_500m);
        harness.PayslipOf(assigned).EmployerCost.ShouldBe(harness.PayslipOf(legacy).EmployerCost);
        harness.PayslipOf(assigned).NetAmount.ShouldBe(harness.PayslipOf(legacy).NetAmount);
        var inputs = harness.StepOf(assigned, "EMPLOYER_DISCOUNT").GetProperty("inputs");
        inputs.GetProperty("incentiveCode").GetString().ShouldBe("SGK_5510_MD81");
        harness.StepOf(assigned, "EMPLOYER_DISCOUNT").GetProperty("parameterRefs")[0].GetProperty("key").GetString().ShouldBe(DiscountKey);
        harness.StepOf(legacy, "EMPLOYER_DISCOUNT").GetProperty("inputs").TryGetProperty("incentiveCode", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ALiteralRateIsUsedAsApprovedTenantData_AndReferencesTheIncentiveRateKey()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee();
        Assign(harness, Define(harness, "SGK_OZEL", IncentiveKind.EmployerSgkRateDiscount, RateParameters(0.10m), "06111"), employee);

        await harness.RunAsync(employee);

        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(5_000m); // 50.000 x 0,10
        harness.StepOf(employee, "EMPLOYER_DISCOUNT").GetProperty("parameterRefs")[0].GetProperty("key").GetString().ShouldBe("Incentive.SGK_OZEL.Rate");
    }

    [Fact]
    public async Task AnUnapprovedDefinition_DoesNotEnterTheCalculation_AndWarnsWithItsCode()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee();
        var control = harness.AddEmployee();
        Assign(harness, Define(harness, "SGK_6111_TASLAK", IncentiveKind.EmployerShareCoverage, CoverageParameters(), "06111", approved: false), employee);

        var summary = await harness.RunAsync(employee, control);

        var warning = WarningsOf(summary, "incentive_not_approved").ShouldHaveSingleItem();
        warning.EmployeeId.ShouldBe(employee);
        warning.Details.ShouldBe("SGK_6111_TASLAK");
        harness.HasStep(employee, "EMPLOYER_INCENTIVE").ShouldBeFalse();
        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(0m);
        harness.StepsJsonOf(employee).ShouldBe(harness.StepsJsonOf(control)); // the trace is the plain employee's, byte for byte
    }

    [Fact]
    public async Task AnSgkAssignment_ReplacesAProfileCodeWithAnotherDeclarationCode_AndWarns()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee("05510");
        Assign(harness, Define(harness, "SGK_OZEL", IncentiveKind.EmployerSgkRateDiscount, RateParameters(0.10m), "06111"), employee);

        var summary = await harness.RunAsync(employee);

        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(5_000m); // the assignment's 10%, NOT the profile code's flat 5 points (2.500)
        var warning = WarningsOf(summary, "incentive_profile_code_ignored").ShouldHaveSingleItem();
        warning.EmployeeId.ShouldBe(employee);
        warning.Details.ShouldBe("05510");
    }

    [Fact]
    public async Task AProfileCodeWithoutLeadingZero_IsTheSameDeclarationCode_AsTheAssignment()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee("5510");
        Assign(harness, Define(harness, "SGK_5510_MD81", IncentiveKind.EmployerSgkRateDiscount, KeyParameters(DiscountKey), "05510"), employee);

        var summary = await harness.RunAsync(employee);

        WarningsOf(summary, "incentive_profile_code_ignored").ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnapprovedSgkAssignment_StillReplacesTheProfileCode_SoNeitherTheModelNorTheFivePointsApply()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee("05510");
        Assign(harness, Define(harness, "SGK_5510_MD81", IncentiveKind.EmployerSgkRateDiscount, KeyParameters(DiscountKey), "05510", approved: false), employee);

        var summary = await harness.RunAsync(employee);

        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(0m);
        WarningsOf(summary, "incentive_not_approved").ShouldHaveSingleItem();
    }

    // ---- (c) and the profile code ----

    [Fact]
    public async Task OnlyATaxAssignment_KeepsTheProfileCodesFivePoints_AndAddsTheRelief()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee("05510", salary: 200_000m);
        var control = harness.AddEmployee("05510", salary: 200_000m);
        Assign(harness, Define(harness, "ARGE_5746_GV", IncentiveKind.IncomeTaxWithholdingRelief, RateParameters(0.8m), null), employee);

        var summary = await harness.RunAsync(employee, control);

        IncentiveWarnings(summary).ShouldBeEmpty();
        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(10_000m); // the profile code's 5 points survive a tax-category assignment
        harness.OutputOf(employee, "INCOME_TAX_RELIEF").ShouldBe(17_030.94m); // see IncentiveCalculatorTests (200.000 brüt, oran 0,8)
        harness.PayslipOf(employee).EmployerCost.ShouldBe(harness.PayslipOf(control).EmployerCost - 17_030.94m);
        harness.PayslipOf(employee).NetAmount.ShouldBe(harness.PayslipOf(control).NetAmount);
    }

    // ---- (d) ----

    [Fact]
    public async Task ADeclarationOnlyAssignment_LeavesTheTraceUntouched_WithoutAnApprovalWarning()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee();
        var control = harness.AddEmployee();
        Assign(harness, Define(harness, "BEYAN_KODU", IncentiveKind.DeclarationOnly, IncentiveParameters.Empty, "07103", approved: false), employee);

        var summary = await harness.RunAsync(employee, control);

        IncentiveWarnings(summary).ShouldBeEmpty();
        harness.StepsJsonOf(employee).ShouldBe(harness.StepsJsonOf(control));
    }

    // ---- skips, duplicates, caching ----

    [Fact]
    public async Task AMissingLegislationKey_SkipsOnlyThatEmployee_WithTheParameterMissingReason()
    {
        var harness = IncentiveServiceHarness.Create();
        var affected = harness.AddEmployee();
        var other = harness.AddEmployee();
        Assign(harness, Define(harness, "SGK_BOZUK", IncentiveKind.EmployerSgkRateDiscount, KeyParameters("Payroll.Incentive.DoesNotExist"), "06111"), affected);

        var summary = await harness.RunAsync(affected, other);

        summary.CalculatedCount.ShouldBe(1);
        var skipped = summary.Skipped.ShouldHaveSingleItem();
        skipped.EmployeeId.ShouldBe(affected);
        skipped.Reason.ShouldBe("incentive_parameter_missing");
    }

    [Fact]
    public async Task ALegislationKeyResolvingToAnImpossibleRate_IsTreatedAsMissing()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee();
        Assign(harness, Define(harness, "SGK_BOZUK", IncentiveKind.EmployerSgkRateDiscount, KeyParameters("Payroll.Sgk.Ceiling"), "06111"), employee); // 247.725 is no rate

        var summary = await harness.RunAsync(employee);

        summary.Skipped.ShouldHaveSingleItem().Reason.ShouldBe("incentive_parameter_missing");
    }

    [Fact]
    public async Task TwoOverlappingAssignmentsOfTheSameCategory_UseTheLatestStartingOne_AndWarn()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee();
        var older = Define(harness, "SGK_ESKI", IncentiveKind.EmployerSgkRateDiscount, RateParameters(0.10m), "06111");
        var newer = Define(harness, "SGK_YENI", IncentiveKind.EmployerSgkRateDiscount, RateParameters(0.20m), "07103");
        Assign(harness, older, employee, new DateOnly(2026, 1, 1));
        Assign(harness, newer, employee, new DateOnly(2026, 2, 1));

        var summary = await harness.RunAsync(employee);

        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(10_000m); // 50.000 x 0,20 from the later-starting SGK_YENI
        var warning = WarningsOf(summary, "incentive_duplicate_assignment").ShouldHaveSingleItem();
        warning.Details.ShouldBe("SGK_YENI");
    }

    [Fact]
    public async Task AnSgkAndATaxAssignmentTogether_AreNotDuplicates()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee(salary: 200_000m);
        Assign(harness, Define(harness, "SGK_5510_MD81", IncentiveKind.EmployerSgkRateDiscount, KeyParameters(DiscountKey), "05510"), employee);
        Assign(harness, Define(harness, "ARGE_5746_GV", IncentiveKind.IncomeTaxWithholdingRelief, RateParameters(0.8m), null), employee);

        var summary = await harness.RunAsync(employee);

        IncentiveWarnings(summary).ShouldBeEmpty();
        harness.OutputOf(employee, "EMPLOYER_DISCOUNT").ShouldBe(10_000m);
        harness.OutputOf(employee, "INCOME_TAX_RELIEF").ShouldBe(17_030.94m);
        harness.PayslipOf(employee).EmployerCost.ShouldBe(217_969.06m);
    }

    [Fact]
    public async Task InactiveOrOutOfRangeDefinitions_AreIgnored_AndTheProfileRuleStaysInForce()
    {
        var harness = IncentiveServiceHarness.Create();
        var inactive = harness.AddEmployee("05510");
        var expired = harness.AddEmployee("05510");
        var passive = Define(harness, "SGK_PASIF", IncentiveKind.EmployerSgkRateDiscount, RateParameters(0.10m), "06111");
        Assign(harness, passive, inactive);
        passive.Deactivate(); // deactivated after the assignment was made (assigning to an inactive definition is refused)
        Assign(harness, Define(harness, "SGK_BITMIS", IncentiveKind.EmployerSgkRateDiscount, RateParameters(0.10m), "06111", validTo: new DateOnly(2026, 2, 28)), expired);

        var summary = await harness.RunAsync(inactive, expired);

        IncentiveWarnings(summary).ShouldBeEmpty();
        harness.OutputOf(inactive, "EMPLOYER_DISCOUNT").ShouldBe(2_500m);
        harness.OutputOf(expired, "EMPLOYER_DISCOUNT").ShouldBe(2_500m);
    }

    [Fact]
    public async Task TheMonthPlanAndEachLegislationKey_AreResolvedOncePerRun_AtThePeriodStart()
    {
        var harness = IncentiveServiceHarness.Create();
        harness.SetLegislation("Payroll.Test.Rate", 0.07m);
        var definition = Define(harness, "SGK_ANAHTAR", IncentiveKind.EmployerSgkRateDiscount, KeyParameters("Payroll.Test.Rate"), "06111");
        var employees = Enumerable.Range(0, 3).Select(_ => harness.AddEmployee()).ToArray();
        foreach (var employee in employees)
        {
            Assign(harness, definition, employee);
        }

        await harness.RunAsync(employees);

        harness.Assignments.CoveringMonthCalls.ShouldBe(1);
        harness.Definitions.ListByIdsCalls.ShouldBe(1);
        await harness.LegislationReader.Received(1).FindAsync("Payroll.Test.Rate", March, Arg.Any<string>(), Arg.Any<CancellationToken>());
        harness.OutputOf(employees[2], "EMPLOYER_DISCOUNT").ShouldBe(3_500m); // 50.000 x 0,07
    }

    // ---- (b), net employees, preview ----

    [Fact]
    public async Task ANetWageEmployeeWithACoverageIncentive_StillKeepsTheAgreedNet_AndTheEmployerCostDrops()
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee(salary: 40_000m, type: EmployeeSalaryType.Net);
        var control = harness.AddEmployee(salary: 40_000m, type: EmployeeSalaryType.Net);
        Assign(harness, Define(harness, "SGK_17103", IncentiveKind.EmployerShareCoverage, CoverageParameters(), "17103"), employee);

        var summary = await harness.RunAsync(employee, control);

        summary.CalculatedCount.ShouldBe(2);
        harness.PayslipOf(employee).NetAmount.ShouldBe(40_000m, 0.01m); // the net target
        harness.PayslipOf(employee).NetAmount.ShouldBe(harness.PayslipOf(control).NetAmount);
        harness.PayslipOf(employee).GrossAmount.ShouldBe(harness.PayslipOf(control).GrossAmount);
        harness.HasStep(employee, "EMPLOYER_INCENTIVE").ShouldBeTrue();
        harness.PayslipOf(employee).EmployerCost.ShouldBeLessThan(harness.PayslipOf(control).EmployerCost);
    }

    [Fact]
    public async Task ThePreviewIncludesTheIncentive_AndStoresNothing()
    {
        var harness = IncentiveServiceHarness.Create();
        var assigned = harness.AddEmployee();
        var control = harness.AddEmployee();
        Assign(harness, Define(harness, "SGK_17103", IncentiveKind.EmployerShareCoverage, CoverageParameters(), "17103"), assigned);

        var preview = await harness.PreviewAsync(assigned, control);

        // Gerçek matrah 50.000, oran 0,155, pay 1: Round(50.000 x 0,155) = 7.750 (< işveren SGK 10.250).
        var assignedLine = preview.Lines.Single(l => l.EmployeeId == assigned);
        var controlLine = preview.Lines.Single(l => l.EmployeeId == control);
        assignedLine.EmployerCost.ShouldBe(controlLine.EmployerCost - 7_750m);
        assignedLine.Net.ShouldBe(controlLine.Net);
        Should.Throw<KeyNotFoundException>(() => harness.PayslipOf(assigned)); // nothing was persisted
    }

    [Fact]
    public async Task ARunWithoutAnyAssignment_ChangesNothing_ExceptTheLegacyDiscountForProfileCodeHolders()
    {
        var harness = IncentiveServiceHarness.Create();
        var plain = harness.AddEmployee();
        var coded = harness.AddEmployee("05510");

        var summary = await harness.RunAsync(plain, coded);

        IncentiveWarnings(summary).ShouldBeEmpty();
        harness.OutputOf(plain, "EMPLOYER_DISCOUNT").ShouldBe(0m);
        harness.OutputOf(coded, "EMPLOYER_DISCOUNT").ShouldBe(2_500m);
        harness.HasStep(coded, "EMPLOYER_INCENTIVE").ShouldBeFalse();
        harness.HasStep(coded, "INCOME_TAX_RELIEF").ShouldBeFalse();
    }
}
