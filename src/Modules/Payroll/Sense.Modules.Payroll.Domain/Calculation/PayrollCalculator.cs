using System.Globalization;
using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Pure gross-to-net payroll engine (11.3.7 "Hesap turnusolu") for one monthly-salaried employee, one month, full
/// worked days (no Attendance input yet - see <see cref="PayrollCalculationInput.WorkedDays"/>), no variable pay
/// items (FM/adjustments: P3/P4). All 4 meal benefit variants are supported (P2, 11.3.07-bordro.md "Yemek varyant
/// kuralları"). No DB access - all statutory values come in via <see cref="PayrollLegislationSnapshot"/>, resolved by
/// the Application layer from <c>ILegislationParameterReader</c> (ADR 0008). NET_TO_GROSS_ITER is out of scope for this slice.
///
/// Minimum-wage exemption design decision (11.3.7 "MINWAGE_EXEMPTION_GV/DV"): rather than seeding separate
/// Payroll.MinimumWage.MonthlyIncomeTaxExemption / MonthlyStampTaxExemption parameters, both exemptions are DERIVED
/// here from the bracket engine and the stamp rate applied to <see cref="PayrollLegislationSnapshot.MinimumWageGross"/>
/// (GVK Geçici md.111 mechanism: the exemption tracks the minimum wage's own tax at the first bracket rate, which is
/// what the law fixes for the whole year regardless of the employee's own cumulative bracket). This keeps one fewer
/// pair of parameters to keep in sync with the bracket table and the minimum wage value.
/// </summary>
public static class PayrollCalculator
{
    public static PayrollCalculationResult Calculate(PayrollCalculationInput input, PayrollLegislationSnapshot p)
    {
        Guard.Against(input.GrossMonthlySalary <= 0m, "GrossMonthlySalary must be positive.");
        Guard.Positive(input.WorkedDays, nameof(input.WorkedDays));
        if (input.Wage is { } w)
        {
            Guard.Against(!PeriodWageMath.IsSupported(w.Basis), "Wage basis must be Daily or Hourly.");
            Guard.Against(w.UnitRate <= 0m, "Wage unit rate must be positive.");
            Guard.Against(w.PaidDays is < 1 or > EmploymentProration.PayrollMonthDays, "Wage paid days must be between 1 and 30.");
        }

        Guard.Against(input.AdditionalTaxableEarnings < 0m, "AdditionalTaxableEarnings cannot be negative.");
        Guard.Against(input.AdvanceDeductionTotal < 0m, "AdvanceDeductionTotal cannot be negative.");
        var payItems = input.PayItems ?? [];
        Guard.Against(payItems.Any(i => i.GrossAmount < 0m), "Pay item amounts cannot be negative.");
        var incentives = input.Incentives ?? [];
        var discountEffect = incentives.SingleOrDefault(i => i.Kind == IncentiveKind.EmployerSgkRateDiscount);
        var coverageEffect = incentives.SingleOrDefault(i => i.Kind == IncentiveKind.EmployerShareCoverage);
        var reliefEffect = incentives.SingleOrDefault(i => i.Kind == IncentiveKind.IncomeTaxWithholdingRelief);
        Guard.Against(discountEffect is not null && coverageEffect is not null, "At most one SGK-category incentive can apply in a month.");
        if (!Enum.IsDefined(input.MealVariant))
        {
            throw new NotSupportedException($"Meal benefit variant '{input.MealVariant}' is not a known value.");
        }

        if (input.DisabilityDegree is < 0 or > 3)
        {
            throw new NotSupportedException($"DisabilityDegree '{input.DisabilityDegree}' is not a known value (0=yok, 1/2/3. derece).");
        }

        var steps = new List<TraceStep>();
        var order = 1;

        // DAYS
        var days = input.WorkedDays;
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.Days, "payroll.trace.step.days",
            Map(("workedDays", days), ("calendarDaysInMonth", input.CalendarDaysInMonth)),
            "workedDays (Attendance eksik gün girdisi yok - tam ay varsayımı, TODO roadmap #23)",
            days, []));

        // PAY_ITEM (formüllü ödeme türleri, PAY-FORMULA-3): her kalem için bir adım, EARNINGS'ten önce. Kalem yoksa hiç adım eklenmez, iz değişmez.
        foreach (var item in payItems)
        {
            steps.Add(PayItemStep(order++, item, p));
        }

        var payItemsTotal = Round(payItems.Sum(i => i.GrossAmount));
        var payItemsSgkExempt = payItems.Where(i => !i.SgkSubject).Sum(i => i.GrossAmount);
        var payItemsGvExempt = payItems.Where(i => !i.IncomeTaxSubject).Sum(i => i.GrossAmount);
        var payItemsStampExempt = payItems.Where(i => !i.StampTaxSubject).Sum(i => i.GrossAmount);

        // EARNINGS (maktu ücret + varsa fazla mesai/hafta tatili/resmî tatil gibi ek vergiye tabi kazançlar, senseik-019 2d)
        // Kıst ücret (işe giriş/çıkış ayı): 30 gün esasıyla orantı; tam ayda 1 (önceki davranış birebir korunur).
        var proration = Proration(input);
        var baseSalary = BaseSalary(input);
        var gross = Round(baseSalary + input.AdditionalTaxableEarnings + payItemsTotal);
        var earningsInputs = Map(("baseSalary", baseSalary), ("additionalTaxableEarnings", input.AdditionalTaxableEarnings), ("grossTotal", gross));
        if (payItems.Count > 0)
        {
            earningsInputs["payItemsTotal"] = Format(payItemsTotal);
        }

        if (input.Wage is { } traced)
        {
            // Hesap izi: brütün nasıl bulunduğu (ücret esası, birim ücret, ücretli gün/saat).
            earningsInputs["wageBasis"] = traced.Basis == WageBasis.Hourly ? "hourly" : "daily";
            earningsInputs["unitRate"] = Format(traced.UnitRate);
            earningsInputs["paidDays"] = traced.PaidDays.ToString(CultureInfo.InvariantCulture);
            if (traced.Basis == WageBasis.Hourly)
            {
                earningsInputs["paidHours"] = Format(traced.PaidDays * PeriodWageMath.DailyWorkingHours);
            }
        }

        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.Earnings, "payroll.trace.step.earnings",
            earningsInputs,
            input.Wage is null
                ? "maktu aylık ücret + fazla mesai/hafta tatili/resmî tatil gibi ek vergiye tabi kazançlar (varsa)"
                : "günlük ücret x ücretli gün (saatlik: saat ücreti x 7,5 x ücretli gün) + ek vergiye tabi kazançlar (varsa)",
            gross, []));

        // Meal benefit (11.3.07-bordro.md "Yemek varyant kuralları", worked-day pro-rated):
        //  - None: no amount, no effect.
        //  - Cash/MealCardOnly: daily-exemption split (GV: fixed daily amount; SGK: rate * daily gross minimum wage),
        //    excess above exemption joins the base; the full amount is paid in net.
        //  - InKind: fully outside both bases (informational only) and not paid in cash (employer provides the meal
        //    directly, no cash reaches the employee) - so it does not add to Net either.
        //  - MealAndMarketCard: paid in net like Cash, but the exemption only applies if the legislation snapshot
        //    says so (current draft: it does not, so the whole amount joins both bases).
        var paysCash = input.MealVariant is MealBenefitVariant.Cash or MealBenefitVariant.MealCardOnly or MealBenefitVariant.MealAndMarketCard;
        var mealTotal = paysCash ? input.MealDailyAmount * days : 0m;
        decimal mealSgkExcess;
        decimal mealGvExcess;
        if (input.MealVariant == MealBenefitVariant.InKind)
        {
            mealSgkExcess = 0m;
            mealGvExcess = 0m;
        }
        else if (input.MealVariant == MealBenefitVariant.MealAndMarketCard && !p.MealMarketCardExemptionApplies)
        {
            mealSgkExcess = mealTotal;
            mealGvExcess = mealTotal;
        }
        else
        {
            var dailyGrossMinimumWage = p.MinimumWageGross / input.CalendarDaysInMonth;
            var mealSgkExemptDaily = p.MealSgkDailyExemptionRate * dailyGrossMinimumWage;
            var mealSgkExemptTotal = Math.Min(mealTotal, mealSgkExemptDaily * days);
            mealSgkExcess = mealTotal - mealSgkExemptTotal;
            var mealGvExemptTotal = Math.Min(mealTotal, p.MealIncomeTaxDailyExemption * days);
            mealGvExcess = mealTotal - mealGvExemptTotal;
        }

        var mealNetAddition = input.MealVariant == MealBenefitVariant.InKind ? 0m : mealTotal;

        // SGK_BASE
        var sgkFloor = Round(p.SgkFloor * proration);
        var sgkCeiling = Round(p.SgkCeiling * proration);
        var sgkBase = Math.Clamp(gross - payItemsSgkExempt + mealSgkExcess, sgkFloor, sgkCeiling);
        var sgkBaseInputs = Map(("gross", gross), ("mealSgkExcess", mealSgkExcess), ("sgkFloor", sgkFloor), ("sgkCeiling", sgkCeiling));
        if (payItems.Count > 0)
        {
            sgkBaseInputs["payItemsSgkExempt"] = Format(payItemsSgkExempt);
        }

        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.SgkBase, "payroll.trace.step.sgk_base",
            sgkBaseInputs,
            payItems.Count > 0
                ? "clamp(gross - SGK'dan muaf ödeme türleri + yemek SGK istisna aşımı, taban, tavan)"
                : "clamp(gross + yemek SGK istisna aşımı, taban, tavan)",
            Round(sgkBase),
            [Ref("Payroll.Sgk.Floor", p.SgkFloor, p), Ref("Payroll.Sgk.Ceiling", p.SgkCeiling, p)]));

        // SGK_EMPLOYEE
        var sgkEmployee = Round(sgkBase * p.SgkEmployeeRate);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.SgkEmployee, "payroll.trace.step.sgk_employee",
            Map(("sgkBase", sgkBase), ("rate", p.SgkEmployeeRate)),
            "sgkBase * SGK işçi oranı",
            sgkEmployee, [Ref("Payroll.Sgk.EmployeeRate", p.SgkEmployeeRate, p)]));

        // UNEMP_EMPLOYEE
        var unempEmployee = Round(sgkBase * p.UnemploymentEmployeeRate);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.UnempEmployee, "payroll.trace.step.unemp_employee",
            Map(("sgkBase", sgkBase), ("rate", p.UnemploymentEmployeeRate)),
            "sgkBase * işsizlik sigortası işçi oranı",
            unempEmployee, [Ref("Payroll.Unemployment.EmployeeRate", p.UnemploymentEmployeeRate, p)]));

        // GV_BASE
        var gvBase = Round(gross - payItemsGvExempt + mealGvExcess - sgkEmployee - unempEmployee);
        var gvBaseInputs = Map(("gross", gross), ("mealGvExcess", mealGvExcess), ("sgkEmployee", sgkEmployee), ("unempEmployee", unempEmployee));
        if (payItems.Count > 0)
        {
            gvBaseInputs["payItemsGvExempt"] = Format(payItemsGvExempt);
        }

        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.GvBase, "payroll.trace.step.gv_base",
            gvBaseInputs,
            payItems.Count > 0
                ? "gross - GV'den muaf ödeme türleri + yemek GV istisna aşımı - SGK_EMPLOYEE - UNEMP_EMPLOYEE"
                : "gross + yemek GV istisna aşımı - SGK_EMPLOYEE - UNEMP_EMPLOYEE",
            gvBase, []));

        // DISABILITY_REDUCTION (GVK md.31, senseik-019 2b): matrah indirimi, MINWAGE_EXEMPTION_GV'den ÖNCE uygulanır.
        var disabilityMonthlyReduction = input.DisabilityDegree switch
        {
            1 => p.DisabilityMonthlyTaxReductionDegree1,
            2 => p.DisabilityMonthlyTaxReductionDegree2,
            3 => p.DisabilityMonthlyTaxReductionDegree3,
            _ => 0m,
        };
        var gvBaseAfterDisability = Math.Max(0m, gvBase - disabilityMonthlyReduction);
        var appliedDisabilityReduction = Round(gvBase - gvBaseAfterDisability);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.DisabilityReduction, "payroll.trace.step.disability_reduction",
            new Dictionary<string, string>
            {
                ["gvBase"] = Format(gvBase),
                ["disabilityDegree"] = input.DisabilityDegree.ToString(CultureInfo.InvariantCulture),
                ["disabilityMonthlyReduction"] = Format(disabilityMonthlyReduction),
            },
            "GVK md.31 engelli çalışan gelir vergisi matrah indirimi (varsa derece bazlı aylık tutar), matrah 0'ın altına inmez",
            appliedDisabilityReduction,
            disabilityMonthlyReduction > 0m
                ? [Ref($"Payroll.Disability.MonthlyTaxReduction.Degree{input.DisabilityDegree}", disabilityMonthlyReduction, p)]
                : []));

        // CUMULATIVE_BASE
        var cumulativeBase = Round(input.PriorCumulativeGvBase + gvBaseAfterDisability);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.CumulativeBase, "payroll.trace.step.cumulative_base",
            Map(("priorCumulativeGvBase", input.PriorCumulativeGvBase), ("gvBaseAfterDisability", gvBaseAfterDisability)),
            "önceki aylar kümülatif matrahı + bu ayın (engelli indirimi sonrası) GV_BASE",
            cumulativeBase, []));

        // INCOME_TAX (bracket breakdown, cumulative method)
        var segments = IncomeTaxMath.Breakdown(p.IncomeTaxBrackets, input.PriorCumulativeGvBase, cumulativeBase);
        var incomeTax = Round(segments.Sum(s => s.Tax));
        var incomeTaxInputs = new Dictionary<string, string>
        {
            ["priorCumulativeGvBase"] = Format(input.PriorCumulativeGvBase),
            ["cumulativeBase"] = Format(cumulativeBase),
        };
        for (var i = 0; i < segments.Count; i++)
        {
            incomeTaxInputs[$"bracket{i + 1}.amount"] = Format(segments[i].Amount);
            incomeTaxInputs[$"bracket{i + 1}.rate"] = Format(segments[i].Rate);
        }

        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.IncomeTax, "payroll.trace.step.income_tax",
            incomeTaxInputs,
            $"TaxUpTo(cumulativeBase) - TaxUpTo(priorCumulativeBase) = {IncomeTaxMath.FormatFormula(segments)}",
            incomeTax,
            segments.Select((s, i) => Ref($"Payroll.IncomeTax.Brackets[{i}]", s.Rate, p)).ToList()));

        // MINWAGE_EXEMPTION_GV (derived: minimum wage's own GV base at the first bracket rate - see type doc)
        var minWageGross = Round(p.MinimumWageGross * proration);
        var minWageGvBase = Round(minWageGross * (1m - p.SgkEmployeeRate - p.UnemploymentEmployeeRate));
        var firstBracketRate = p.IncomeTaxBrackets[0].Rate;
        var minWageExemptionGv = Round(minWageGvBase * firstBracketRate);
        var incomeTaxAfterExemption = Math.Max(0m, incomeTax - minWageExemptionGv);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.MinWageExemptionGv, "payroll.trace.step.minwage_exemption_gv",
            Map(("minimumWageGross", minWageGross), ("minWageGvBase", minWageGvBase), ("firstBracketRate", firstBracketRate), ("incomeTaxBeforeExemption", incomeTax)),
            "min(incomeTax, asgari ücret GV matrahı * ilk dilim oranı)",
            Round(incomeTax - incomeTaxAfterExemption),
            [Ref("Payroll.MinimumWage.Gross", p.MinimumWageGross, p)]));

        // STAMP_TAX
        var stampTax = Round((gross - payItemsStampExempt) * p.StampTaxRate);
        var stampTaxInputs = Map(("gross", gross), ("rate", p.StampTaxRate));
        if (payItems.Count > 0)
        {
            stampTaxInputs["payItemsStampExempt"] = Format(payItemsStampExempt);
        }

        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.StampTax, "payroll.trace.step.stamp_tax",
            stampTaxInputs,
            payItems.Count > 0 ? "(gross - damgadan muaf ödeme türleri) * damga vergisi oranı" : "gross * damga vergisi oranı",
            stampTax, [Ref("Payroll.StampTax.Rate", p.StampTaxRate, p)]));

        // MINWAGE_EXEMPTION_DV
        var minWageStampExemption = Round(minWageGross * p.StampTaxRate);
        var stampTaxAfterExemption = Math.Max(0m, stampTax - minWageStampExemption);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.MinWageExemptionDv, "payroll.trace.step.minwage_exemption_dv",
            Map(("minimumWageGross", minWageGross), ("rate", p.StampTaxRate), ("stampTaxBeforeExemption", stampTax)),
            "min(stampTax, asgari ücret * damga vergisi oranı)",
            Round(stampTax - stampTaxAfterExemption),
            [Ref("Payroll.MinimumWage.Gross", p.MinimumWageGross, p), Ref("Payroll.StampTax.Rate", p.StampTaxRate, p)]));

        // NET_BEFORE_DEDUCTIONS (masraf iadesi vergisiz eklenir - TB-7 Expense köprüsü, S-13; SGK/GV/damga matrahlarına
        // hiç girmez). İİK m.83 icra tavanı bu tutar üzerinden hesaplandığı için DEDUCTIONS'tan önce bulunur.
        var expenseReimbursement = input.ExpenseReimbursement;
        var netBeforeDeductions = Round(gross - sgkEmployee - unempEmployee - incomeTaxAfterExemption - stampTaxAfterExemption + mealNetAddition + expenseReimbursement);

        // DEDUCTIONS (senseik-019 2c: icra/nafaka + avans mahsubu; #58: sendika/dayanışma aidatı -
        // UnionDuesCalculator ile Application katmanında hesaplanıp buraya girdi olarak geliyor, motor DB'ye
        // bakmıyor, İİK m.83 tavanına tabi değil; BES kesintisi hâlâ kapsam dışı)
        var garnishmentOrders = input.GarnishmentOrders ?? [];
        var garnishmentResult = GarnishmentCalculator.Calculate(netBeforeDeductions, garnishmentOrders);
        var advanceDeduction = Math.Min(Math.Max(0m, netBeforeDeductions - garnishmentResult.TotalDeduction), input.AdvanceDeductionTotal);
        var unionDues = input.UnionDuesAmount;
        var deductionsTotal = Round(garnishmentResult.TotalDeduction + advanceDeduction + unionDues);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.Deductions, "payroll.trace.step.deductions",
            Map(("netBeforeDeductions", netBeforeDeductions), ("garnishmentDeductionTotal", garnishmentResult.TotalDeduction), ("advanceDeductionTotal", advanceDeduction), ("unionDues", unionDues)),
            "icra/nafaka (İİK m.83, GarnishmentCalculator) + avans mahsubu (kalan nete kadar) + sendika/dayanışma aidatı - BES kapsam dışı (P6+)",
            deductionsTotal, []));

        // NET
        var net = Round(netBeforeDeductions - deductionsTotal);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.Net, "payroll.trace.step.net",
            Map(("gross", gross), ("sgkEmployee", sgkEmployee), ("unempEmployee", unempEmployee),
                ("incomeTaxAfterExemption", incomeTaxAfterExemption), ("stampTaxAfterExemption", stampTaxAfterExemption),
                ("mealNetAddition", mealNetAddition), ("expenseReimbursement", expenseReimbursement),
                ("deductionsTotal", deductionsTotal)),
            "gross - SGK_EMPLOYEE - UNEMP_EMPLOYEE - INCOME_TAX(istisna sonrası) - STAMP_TAX(istisna sonrası) + nakit ödenen yemek (ayni hariç) + masraf iadesi (vergisiz) - DEDUCTIONS (icra/nafaka/avans/sendika aidatı)",
            net, []));

        // EMPLOYER_SGK
        var employerSgk = Round(sgkBase * p.SgkEmployerRate);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.EmployerSgk, "payroll.trace.step.employer_sgk",
            Map(("sgkBase", sgkBase), ("rate", p.SgkEmployerRate)),
            "sgkBase * SGK işveren oranı",
            employerSgk, [Ref("Payroll.Sgk.EmployerRate", p.SgkEmployerRate, p)]));

        // EMPLOYER_DISCOUNT: (a) an approved rate-discount incentive replaces the legacy rule (senseik-019 2a: 5510 md.81 5 puanlık teşvik,
        // profildeki IncentiveLawCode doluysa uygulanır - bkz. PayrollCalculationInput.IncentiveEligible doc). Without an (a) effect this is the legacy step, unchanged.
        decimal employerDiscount;
        if (discountEffect is not null)
        {
            var (amount, discountStep) = IncentiveSteps.EmployerDiscount(order++, discountEffect, sgkBase, employerSgk, p);
            employerDiscount = amount;
            steps.Add(discountStep);
        }
        else
        {
            employerDiscount = input.IncentiveEligible ? Round(sgkBase * p.SgkEmployerDiscountRate) : 0m;
            steps.Add(new TraceStep(
                order++, PayrollTraceStepCodes.EmployerDiscount, "payroll.trace.step.employer_discount",
                new Dictionary<string, string>
                {
                    ["sgkBase"] = Format(sgkBase),
                    ["discountRate"] = Format(p.SgkEmployerDiscountRate),
                    ["incentiveEligible"] = input.IncentiveEligible ? "1" : "0",
                },
                input.IncentiveEligible
                    ? "sgkBase * 5 puanlık SGK işveren teşvik oranı"
                    : "teşvike uygun değil (IncentiveLawCode boş) - Output=0",
                employerDiscount,
                input.IncentiveEligible ? [Ref("Payroll.Sgk.EmployerDiscountRate", p.SgkEmployerDiscountRate, p)] : []));
        }

        // EMPLOYER_UNEMP
        var employerUnemp = Round(sgkBase * p.UnemploymentEmployerRate);
        steps.Add(new TraceStep(
            order++, PayrollTraceStepCodes.EmployerUnemp, "payroll.trace.step.employer_unemp",
            Map(("sgkBase", sgkBase), ("rate", p.UnemploymentEmployerRate)),
            "sgkBase * işsizlik sigortası işveren oranı",
            employerUnemp, [Ref("Payroll.Unemployment.EmployerRate", p.UnemploymentEmployerRate, p)]));

        // EMPLOYER_INCENTIVE (b): premiums the state covers; only when such an incentive is in force, so the trace is otherwise unchanged.
        var employerIncentive = 0m;
        if (coverageEffect is not null)
        {
            var figures = new PremiumFigures(sgkBase, employerSgk, employerDiscount, employerUnemp, sgkEmployee, unempEmployee);
            var (amount, incentiveStep) = IncentiveSteps.EmployerIncentive(order++, coverageEffect, figures, proration, p);
            employerIncentive = amount;
            steps.Add(incentiveStep);
        }

        // INCOME_TAX_RELIEF (c): share of the income tax withheld that the state remits (terkin); the employee's net is not touched.
        var incomeTaxRelief = 0m;
        if (reliefEffect is not null)
        {
            var (amount, reliefStep) = IncentiveSteps.IncomeTaxRelief(order++, reliefEffect, incomeTaxAfterExemption, p);
            incomeTaxRelief = amount;
            steps.Add(reliefStep);
        }

        // EMPLOYER_COST
        var employerCost = Round(gross + employerSgk - employerDiscount + employerUnemp - employerIncentive - incomeTaxRelief);
        var employerCostInputs = Map(("gross", gross), ("employerSgk", employerSgk), ("employerDiscount", employerDiscount), ("employerUnemp", employerUnemp));
        var employerCostFormula = "gross + EMPLOYER_SGK - EMPLOYER_DISCOUNT + EMPLOYER_UNEMP";
        if (coverageEffect is not null)
        {
            employerCostInputs["employerIncentive"] = Format(employerIncentive);
            employerCostFormula += " - EMPLOYER_INCENTIVE";
        }

        if (reliefEffect is not null)
        {
            employerCostInputs["incomeTaxRelief"] = Format(incomeTaxRelief);
            employerCostFormula += " - INCOME_TAX_RELIEF";
        }

        steps.Add(new TraceStep(
            order, PayrollTraceStepCodes.EmployerCost, "payroll.trace.step.employer_cost",
            employerCostInputs,
            employerCostFormula,
            employerCost, []));

        return new PayrollCalculationResult(steps, Round(gross), net, employerCost, garnishmentResult.AppliedAmounts);
    }

    /// <summary>
    /// The period's base wage the pay-item formulas call <c>brut</c>: monthly salary (prorated for a partial month) or, for a
    /// daily/hourly employee, the wage x paid days. Overtime, other earnings and pay items are NOT included. The same figure
    /// <see cref="Calculate"/> uses as its <c>baseSalary</c>.
    /// </summary>
    public static decimal BaseSalary(PayrollCalculationInput input)
    {
        var proration = Proration(input);
        return input.Wage is { } wage
            ? PeriodWageMath.PeriodGross(wage.Basis, wage.UnitRate, wage.PaidDays) // günlük/saatlik: brüt zaten ücretli güne göre, ikinci oranlama yok
            : proration == 1m ? input.GrossMonthlySalary : Round(input.GrossMonthlySalary * proration);
    }

    private static decimal Proration(PayrollCalculationInput input) =>
        input.ProratedPaidDays is { } paidDays and > 0 and < 30 ? paidDays / 30m : 1m;

    private static TraceStep PayItemStep(int order, PayItemEarning item, PayrollLegislationSnapshot p)
    {
        var inputs = new Dictionary<string, string>
        {
            ["code"] = item.Code,
            ["name"] = item.Name,
            ["amountBasis"] = item.NetTarget is null ? "gross" : "net",
            ["sgkSubject"] = item.SgkSubject ? "1" : "0",
            ["incomeTaxSubject"] = item.IncomeTaxSubject ? "1" : "0",
            ["stampTaxSubject"] = item.StampTaxSubject ? "1" : "0",
        };
        foreach (var (name, value) in item.UsedVariables.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            inputs[$"variable.{name}"] = Format(value);
        }

        if (item.NetTarget is { } netTarget)
        {
            inputs["netTarget"] = Format(netTarget);
        }

        IReadOnlyList<TraceParameterRef> refs = item.UsedVariables.ContainsKey("asgari_ucret")
            ? [Ref("Payroll.MinimumWage.Gross", p.MinimumWageGross, p)]
            : [];
        return new TraceStep(order, PayrollTraceStepCodes.PayItem, "payroll.trace.step.pay_item", inputs, item.Formula, item.GrossAmount, refs, item.Code);
    }

    internal static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Map(params (string Key, decimal Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => Format(p.Value));

    private static Dictionary<string, string> Map(params (string Key, int Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value.ToString(CultureInfo.InvariantCulture));

    internal static TraceParameterRef Ref(string key, decimal value, PayrollLegislationSnapshot p) => new(key, value, p.AsOf);
}
