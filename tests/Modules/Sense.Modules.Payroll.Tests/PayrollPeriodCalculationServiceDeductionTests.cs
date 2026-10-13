using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Payslips;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

// PAY-DEDUCTION-1: formüllü kesintiler (Direction = Deduction) in the period run. Aylık ücretli 50,000 brüt (Eylül, 30 gün) unless stated.
// The service helpers (RunWithPayItemsAsync, NewAssignment, ...) are the PAY-FORMULA-3 ones in the main file.
public sealed partial class PayrollPeriodCalculationServiceTests
{
    private static PayItemDefinition NewDeduction(string code, string formula) =>
        PayItemDefinition.Create(
            TenantId, code,
            new PayItemDefinitionData(code + " adı", PayItemDirection.Deduction, PayItemAmountBasis.Gross, false, false, false, formula, null, new DateOnly(2026, 1, 1), null)).Value;

    private static IEnumerable<PayrollWarning> Warnings(PayItemRun run, string code) => run.Summary.Warnings.Where(w => w.Code == code);

    [Fact]
    public async Task Deductions_BrutTimesOnePercent_LowersTheNetByTheAmount_AndLeavesGrossAndBasesAlone()
    {
        var baseline = await RunWithPayItemsAsync([]);

        // brut * 0.01 = 50,000 x 0.01 = 500.00
        var run = await RunWithPayItemsAsync([(NewDeduction("KOOP", "brut * 0.01"), null)]);

        run.Summary.CalculatedCount.ShouldBe(1);
        run.Payslip!.GrossAmount.ShouldBe(baseline.Payslip!.GrossAmount);
        run.Payslip.NetAmount.ShouldBe(baseline.Payslip.NetAmount - 500m);
        run.Payslip.EmployerCost.ShouldBe(baseline.Payslip.EmployerCost);
        foreach (var code in new[] { PayrollTraceStepCodes.Earnings, PayrollTraceStepCodes.SgkBase, PayrollTraceStepCodes.GvBase, PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.StampTax })
        {
            var step = run.Steps.Single(s => s.Code == code);
            var old = baseline.Steps.Single(s => s.Code == code);
            step.Output.ShouldBe(old.Output, code);
            step.Inputs.ShouldBe(old.Inputs, code);
        }

        var item = PayItemStepOf(run, "KOOP");
        item.Inputs["direction"].ShouldBe("deduction");
        item.Inputs["deductionAmount"].ShouldBe("500");
        item.Inputs["variable.brut"].ShouldBe("50000");
        item.Output.ShouldBe(500m);
        run.Steps.Single(s => s.Code == PayrollTraceStepCodes.Deductions).Inputs["payItemDeductionsTotal"].ShouldBe("500");
        run.Steps.Single(s => s.Code == PayrollTraceStepCodes.Earnings).Inputs.ShouldNotContainKey("payItemsTotal");
        Warnings(run, "pay_item_deduction_capped").ShouldBeEmpty();
    }

    [Fact]
    public async Task Deductions_WithoutAny_TheTraceHasNoDeductionTraces()
    {
        var run = await RunWithPayItemsAsync([]);

        run.Steps.ShouldNotContain(s => s.Code == PayrollTraceStepCodes.PayItem);
        run.Steps.Single(s => s.Code == PayrollTraceStepCodes.Deductions).Inputs.ShouldNotContainKey("payItemDeductionsTotal");
    }

    [Fact]
    public async Task Deductions_EarningAndDeductionTogether_GrossGrowsByTheEarningOnly()
    {
        var earningOnly = await RunWithPayItemsAsync([(NewPayItem("YEMEK", "deger"), 5_000m)]);

        var both = await RunWithPayItemsAsync([(NewPayItem("YEMEK", "deger"), 5_000m), (NewDeduction("KOOP", "brut * 0.01"), null)]);

        both.Payslip!.GrossAmount.ShouldBe(55_000m);
        both.Payslip.NetAmount.ShouldBe(earningOnly.Payslip!.NetAmount - 500m); // "brut" excludes the earning item: still 50,000 x 0.01
        both.Steps.Where(s => s.Code == PayrollTraceStepCodes.PayItem).Select(s => s.PayItem).ShouldBe(["YEMEK", "KOOP"]);
    }

    [Fact]
    public async Task Deductions_BiggerThanTheNet_GiveZeroNet_AndTheCappedWarning()
    {
        var run = await RunWithPayItemsAsync([(NewDeduction("DEVKES", "deger"), 900_000_000m)]);

        run.Summary.CalculatedCount.ShouldBe(1);
        run.Payslip!.NetAmount.ShouldBe(0m);
        run.Payslip.GrossAmount.ShouldBe(50_000m);
        var warning = Warnings(run, "pay_item_deduction_capped").ShouldHaveSingleItem();
        warning.Details.ShouldBe("DEVKES"); // yalnız kalem kodu
        PayItemStepOf(run, "DEVKES").Inputs["deductionAmount"].ShouldBe("900000000");
    }

    [Fact]
    public async Task Deductions_Capping_StartsFromTheLastCode_AndTheWarningNamesTheFirstCutOne()
    {
        var baseline = await RunWithPayItemsAsync([]);
        var net = baseline.Payslip!.NetAmount;

        // A takes 1,000, B takes 900,000,000: B is cut first (to net - 1,000), A stays whole.
        var run = await RunWithPayItemsAsync([(NewDeduction("B_KES", "deger"), 900_000_000m), (NewDeduction("A_KES", "deger"), 1_000m)]);

        run.Payslip!.NetAmount.ShouldBe(0m);
        PayItemStepOf(run, "A_KES").Output.ShouldBe(1_000m);
        PayItemStepOf(run, "B_KES").Output.ShouldBe(net - 1_000m);
        Warnings(run, "pay_item_deduction_capped").ShouldHaveSingleItem().Details.ShouldBe("B_KES");
    }

    [Fact]
    public async Task Deductions_NegativeResult_IsZeroWithTheExistingWarning_AndNoCap()
    {
        var baseline = await RunWithPayItemsAsync([]);

        var run = await RunWithPayItemsAsync([(NewDeduction("EKSI", "brut - deger"), 70_000m)]);

        PayItemStepOf(run, "EKSI").Output.ShouldBe(0m);
        run.Payslip!.NetAmount.ShouldBe(baseline.Payslip!.NetAmount);
        Warnings(run, "pay_item_negative_result").ShouldHaveSingleItem().Details.ShouldBe("EKSI");
        Warnings(run, "pay_item_deduction_capped").ShouldBeEmpty();
    }

    [Fact]
    public async Task Deductions_FormulaFailure_SkipsTheEmployeeLikeAnEarning()
    {
        var run = await RunWithPayItemsAsync([(NewDeduction("BOLME", "brut / (deger - deger)"), 5m)]);

        run.Summary.CalculatedCount.ShouldBe(0);
        run.Summary.Skipped.ShouldHaveSingleItem().Reason.ShouldBe(PayrollSkipReasons.PayItemFormulaFailed);
        run.Payslip.ShouldBeNull();
    }

    [Fact]
    public async Task Deductions_DoNotEnterTheNetToGrossSolver_TheNetItemGrossIsTheSame()
    {
        var netItem = NewPayItem("NETPRIM", "deger", PayItemAmountBasis.Net);
        var withNetItem = await RunWithPayItemsAsync([(netItem, 10_000m)]);

        var withBoth = await RunWithPayItemsAsync([(netItem, 10_000m), (NewDeduction("KOOP", "brut * 0.01"), null)]);

        PayItemStepOf(withBoth, "NETPRIM").Output.ShouldBe(PayItemStepOf(withNetItem, "NETPRIM").Output);
        withBoth.Payslip!.GrossAmount.ShouldBe(withNetItem.Payslip!.GrossAmount);
        withBoth.Payslip.NetAmount.ShouldBe(withNetItem.Payslip.NetAmount - 500m);
    }

    [Fact]
    public async Task Deductions_PartialMonth_UsesTheKistPaidDays()
    {
        // 16 Eylül'de işe giriş: ucretli_gun = 15 -> 15 x 10 = 150.
        var baseline = await RunWithPayItemsAsync([], given: (p, s, id) => GivenEmployee(p, s, id, Salary(50_000m), hireDate: new DateOnly(2026, 9, 16)));

        var run = await RunWithPayItemsAsync(
            [(NewDeduction("GUNKES", "ucretli_gun * 10"), null)],
            given: (p, s, id) => GivenEmployee(p, s, id, Salary(50_000m), hireDate: new DateOnly(2026, 9, 16)));

        PayItemStepOf(run, "GUNKES").Output.ShouldBe(150m);
        run.Payslip!.NetAmount.ShouldBe(baseline.Payslip!.NetAmount - 150m);
        run.Payslip.GrossAmount.ShouldBe(baseline.Payslip.GrossAmount);
    }

    [Fact]
    public async Task Deductions_DailyWage_UsesThePaidWageDays()
    {
        // Günlük 2,000 x 30 ücretli gün; ucretli_gun x 10 = 300.
        Action<IEmployeePayrollProfileRepository, IEmployeeSalaryLookup, Guid> daily =
            (p, s, id) => GivenWageEmployee(p, s, id, WageBasis.Daily, WageSalary(EmployeePayFrequency.Daily, 2_000m));
        var baseline = await RunWithPayItemsAsync([], given: daily);

        var run = await RunWithPayItemsAsync([(NewDeduction("GUNKES", "ucretli_gun * 10"), null)], given: daily);

        PayItemStepOf(run, "GUNKES").Output.ShouldBe(300m);
        run.Payslip!.NetAmount.ShouldBe(baseline.Payslip!.NetAmount - 300m);
    }

    [Fact]
    public async Task Deductions_Preview_IncludesThemAndWritesNothing()
    {
        var baseline = await RunWithPayItemsAsync([]);
        var employeeId = Guid.NewGuid();
        var item = NewDeduction("KOOP", "brut * 0.01");
        var (assignmentRepository, definitionRepository) = PayItemRepositories([NewAssignment(employeeId, item, null)], [item]);
        var service = BuildService(
            out var payslips, out var traces, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _,
            payItemAssignmentsOverride: assignmentRepository, payItemDefinitionsOverride: definitionRepository);
        GivenEmployee(profiles, salaryLookup, employeeId, Salary(50_000m));

        var preview = await service.PreviewAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        var line = preview.Lines.ShouldHaveSingleItem();
        line.Gross.ShouldBe(baseline.Payslip!.GrossAmount);
        line.Net.ShouldBe(baseline.Payslip.NetAmount - 500m);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
        traces.DidNotReceive().Add(Arg.Any<PayslipCalculationTrace>());
    }

    [Fact]
    public async Task Deductions_Preview_ReportsTheCappedWarning()
    {
        var employeeId = Guid.NewGuid();
        var item = NewDeduction("DEVKES", "deger");
        var (assignmentRepository, definitionRepository) = PayItemRepositories([NewAssignment(employeeId, item, 900_000_000m)], [item]);
        var service = BuildService(
            out _, out _, out var profiles, out _, out var salaryLookup, out _, out _, out _, out _,
            payItemAssignmentsOverride: assignmentRepository, payItemDefinitionsOverride: definitionRepository);
        GivenEmployee(profiles, salaryLookup, employeeId, Salary(50_000m));

        var preview = await service.PreviewAsync(SeptemberPeriod(), [employeeId], CancellationToken.None);

        preview.Lines.ShouldHaveSingleItem().Net.ShouldBe(0m);
        preview.Summary.Warnings.ShouldContain(w => w.Code == "pay_item_deduction_capped" && w.Details == "DEVKES");
    }
}
