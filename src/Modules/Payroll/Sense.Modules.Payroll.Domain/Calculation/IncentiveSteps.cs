using Sense.Modules.Payroll.Domain.Incentives;
using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Trace steps and amounts of the incentive models (PAY-INCENTIVE-PLAN "Hesaba entegrasyon"), kept out of
/// <see cref="PayrollCalculator"/> so the pre-existing path stays untouched. Each helper returns the amount and its trace step; none of
/// them touches the employee side of the payslip: incentives only change the employer cost.
/// </summary>
internal static class IncentiveSteps
{
    /// <summary>(a) EMPLOYER_DISCOUNT: <c>Round(sgkBase x rate)</c>, replaces the legacy 5-point rule for this employee.</summary>
    public static (decimal Amount, TraceStep Step) EmployerDiscount(int order, IncentiveEffectInput effect, decimal sgkBase, decimal employerSgk, PayrollLegislationSnapshot p)
    {
        var rate = RequireRate(effect);
        var requested = PayrollCalculator.Round(sgkBase * rate.Value);
        var capped = requested > employerSgk; // a wrong, too large rate cannot push the employer SGK premium below zero
        var amount = capped ? employerSgk : requested;
        var inputs = new Dictionary<string, string>
        {
            ["sgkBase"] = PayrollCalculator.Format(sgkBase),
            ["discountRate"] = PayrollCalculator.Format(rate.Value),
            ["incentiveEligible"] = "1",
            ["incentiveCode"] = effect.Code,
        };
        if (capped)
        {
            inputs["employerSgk"] = PayrollCalculator.Format(employerSgk);
            inputs["cappedAtEmployerSgk"] = "1";
        }

        var step = new TraceStep(
            order, PayrollTraceStepCodes.EmployerDiscount, "payroll.trace.step.employer_discount",
            inputs,
            capped
                ? $"min(EMPLOYER_SGK, sgkBase * teşvik indirim oranı) ({effect.Code}) - oran işveren SGK payını aştığı için işveren SGK payıyla sınırlandı"
                : $"sgkBase * teşvik indirim oranı ({effect.Code})",
            amount,
            [PayrollCalculator.Ref(rate.ParameterKey, rate.Value, p)]);
        return (amount, step);
    }

    /// <summary>
    /// (b) EMPLOYER_INCENTIVE: the premiums the state covers. Base = the actual SGK base, or <c>min(sgkBase, Round(minimumWage x cap x proration))</c>;
    /// each covered premium is <c>min(premium owed, Round(base x rate x share))</c>, so a covered amount never exceeds the premium itself.
    /// </summary>
    public static (decimal Amount, TraceStep Step) EmployerIncentive(
        int order, IncentiveEffectInput effect, PremiumFigures figures, decimal proration, PayrollLegislationSnapshot p)
    {
        var share = RequireRate(effect);
        Guard.Against(effect.CoverageBase is null, "A premium coverage incentive needs a coverage base.");
        Guard.Against(effect.CoversEmployerSgk && effect.EmployerSgkRate is null, "Covering the employer SGK premium needs its rate.");

        var usesMinimumWage = effect.CoverageBase == IncentiveCoverageBase.MinimumWage;
        var cap = effect.CapMultiplier ?? 0m;
        Guard.Against(usesMinimumWage && cap <= 0m, "A minimum-wage coverage base needs a positive cap multiplier.");
        var minimumWageCap = usesMinimumWage ? PayrollCalculator.Round(p.MinimumWageGross * cap * proration) : 0m;
        var coverageBase = usesMinimumWage ? Math.Min(figures.SgkBase, minimumWageCap) : figures.SgkBase;

        var employerSgkCovered = effect.CoversEmployerSgk
            ? Math.Min(Math.Max(0m, figures.EmployerSgk - figures.EmployerDiscount), PayrollCalculator.Round(coverageBase * effect.EmployerSgkRate!.Value * share.Value))
            : 0m;
        var employerUnempCovered = effect.CoversEmployerUnemployment
            ? Math.Min(figures.EmployerUnemp, PayrollCalculator.Round(coverageBase * p.UnemploymentEmployerRate * share.Value))
            : 0m;
        var employeeSgkCovered = effect.CoversEmployeeSgk
            ? Math.Min(figures.SgkEmployee, PayrollCalculator.Round(coverageBase * p.SgkEmployeeRate * share.Value))
            : 0m;
        var employeeUnempCovered = effect.CoversEmployeeUnemployment
            ? Math.Min(figures.UnempEmployee, PayrollCalculator.Round(coverageBase * p.UnemploymentEmployeeRate * share.Value))
            : 0m;
        var amount = PayrollCalculator.Round(employerSgkCovered + employerUnempCovered + employeeSgkCovered + employeeUnempCovered);

        var inputs = new Dictionary<string, string>
        {
            ["incentiveCode"] = effect.Code,
            ["coverageBase"] = usesMinimumWage ? "minimumWage" : "actualSgkBase",
            ["sgkBase"] = PayrollCalculator.Format(figures.SgkBase),
            ["share"] = PayrollCalculator.Format(share.Value),
        };
        var refs = new List<TraceParameterRef> { PayrollCalculator.Ref(share.ParameterKey, share.Value, p) };
        if (usesMinimumWage)
        {
            inputs["capMultiplier"] = PayrollCalculator.Format(cap);
            inputs["proration"] = PayrollCalculator.Format(proration);
            inputs["minimumWageCap"] = PayrollCalculator.Format(minimumWageCap);
            refs.Add(PayrollCalculator.Ref("Payroll.MinimumWage.Gross", p.MinimumWageGross, p));
        }

        inputs["premiumBase"] = PayrollCalculator.Format(coverageBase);
        if (effect.CoversEmployerSgk)
        {
            inputs["employerSgkRate"] = PayrollCalculator.Format(effect.EmployerSgkRate!.Value);
            inputs["employerSgkCovered"] = PayrollCalculator.Format(employerSgkCovered);
            refs.Add(PayrollCalculator.Ref(effect.EmployerSgkRate.ParameterKey, effect.EmployerSgkRate.Value, p));
        }

        if (effect.CoversEmployerUnemployment)
        {
            inputs["employerUnempCovered"] = PayrollCalculator.Format(employerUnempCovered);
            refs.Add(PayrollCalculator.Ref("Payroll.Unemployment.EmployerRate", p.UnemploymentEmployerRate, p));
        }

        if (effect.CoversEmployeeSgk)
        {
            inputs["employeeSgkCovered"] = PayrollCalculator.Format(employeeSgkCovered);
            refs.Add(PayrollCalculator.Ref("Payroll.Sgk.EmployeeRate", p.SgkEmployeeRate, p));
        }

        if (effect.CoversEmployeeUnemployment)
        {
            inputs["employeeUnempCovered"] = PayrollCalculator.Format(employeeUnempCovered);
            refs.Add(PayrollCalculator.Ref("Payroll.Unemployment.EmployeeRate", p.UnemploymentEmployeeRate, p));
        }

        var step = new TraceStep(
            order, PayrollTraceStepCodes.EmployerIncentive, "payroll.trace.step.employer_incentive",
            inputs,
            $"devletçe karşılanan prim toplamı ({effect.Code}): her prim için min(prim tutarı, prim tabanı * oran * karşılama payı)",
            amount, refs);
        return (amount, step);
    }

    /// <summary>(c) INCOME_TAX_RELIEF: <c>Round(incomeTaxAfterExemption x rate)</c>; assumed to be remitted by the state, so the employee's net is unchanged.</summary>
    public static (decimal Amount, TraceStep Step) IncomeTaxRelief(int order, IncentiveEffectInput effect, decimal incomeTaxAfterExemption, PayrollLegislationSnapshot p)
    {
        var rate = RequireRate(effect);
        var amount = PayrollCalculator.Round(incomeTaxAfterExemption * rate.Value);
        var step = new TraceStep(
            order, PayrollTraceStepCodes.IncomeTaxRelief, "payroll.trace.step.income_tax_relief",
            new Dictionary<string, string>
            {
                ["incentiveCode"] = effect.Code,
                ["incomeTaxAfterExemption"] = PayrollCalculator.Format(incomeTaxAfterExemption),
                ["rate"] = PayrollCalculator.Format(rate.Value),
            },
            $"gelir vergisi (asgari ücret istisnası sonrası) * teşvik payı ({effect.Code}); terkin varsayımı: çalışanın neti değişmez",
            amount,
            [PayrollCalculator.Ref(rate.ParameterKey, rate.Value, p)]);
        return (amount, step);
    }

    private static ResolvedRate RequireRate(IncentiveEffectInput effect)
    {
        Guard.Against(effect.Rate is null, $"Incentive '{effect.Code}' has no resolved rate.");
        return effect.Rate!;
    }
}

/// <summary>The premium amounts already worked out by the calculator that a coverage incentive caps its coverage against.</summary>
internal readonly record struct PremiumFigures(
    decimal SgkBase, decimal EmployerSgk, decimal EmployerDiscount, decimal EmployerUnemp, decimal SgkEmployee, decimal UnempEmployee);
