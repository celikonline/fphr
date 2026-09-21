using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>Which kind of payment a "Çıkış hesabı" line is; decides the default tax treatment (<see cref="ExitSettlementNetCalculator.DefaultTreatment"/>).</summary>
public enum ExitPayKind
{
    /// <summary>Kıdem tazminatı.</summary>
    Severance = 0,

    /// <summary>İhbar tazminatı.</summary>
    Notice = 1,

    /// <summary>Kullanılmamış yıllık izin ücreti (İş K. m.59).</summary>
    UnusedLeave = 2,

    /// <summary>İkale (anlaşmalı fesih) bedeli: a contract-agreed amount, not computed by law.</summary>
    MutualRescissionPay = 3,

    /// <summary>Anlaşma bedeline ek olarak ödenen N aylık ücret vb.</summary>
    ExtraPayment = 4,

    /// <summary>Çıkış ayının ücreti ve ek ödemeleri (anlaşma bedelinden ayrı ödenir).</summary>
    FinalMonthWage = 5,

    Other = 6,
}

/// <summary>Which deductions apply to one line. Editable per line because the right treatment of contract-specific payments needs an accountant's call.</summary>
public sealed record ExitPayTaxTreatment(bool SgkSubject, bool IncomeTaxSubject, bool StampTaxSubject);

/// <summary>One gross line to net down; <paramref name="Key"/> ties the result back to the caller's line.</summary>
public sealed record ExitPayInput(Guid Key, decimal Gross, ExitPayTaxTreatment Treatment);

public sealed record ExitPayLineResult(Guid Key, decimal Gross, decimal Sgk, decimal IncomeTax, decimal StampTax, decimal Net);

public sealed record ExitNetResult(
    IReadOnlyList<ExitPayLineResult> Lines,
    decimal GrossTotal,
    decimal SgkTotal,
    decimal IncomeTaxTotal,
    decimal StampTaxTotal,
    decimal NetTotal);

/// <summary>
/// Pure gross-to-net for the lines of one exit ("Çıkış hesabı"): SGK işçi payı + işsizlik primi (monthly ceiling), cumulative
/// income tax on the SGK-reduced base (brackets from <see cref="PayrollLegislationSnapshot"/>, never hardcoded) and damga
/// vergisi. All lines are treated as paid in the exit month, so the tax is the bracket difference between the cumulative
/// base before and after them. Estimate only: the default treatments below (and the kıdem tazminatı gelir vergisi
/// istisnası, not modelled) need SMMM confirmation before figures are paid out.
/// </summary>
public static class ExitSettlementNetCalculator
{
    /// <summary>
    /// Kıdem: damga vergisi only. İhbar: gelir + damga vergisi, no SGK. İzin ücreti and çıkış ayı ücreti: SGK + gelir + damga.
    /// İkale bedeli / ek ödeme / other: gelir + damga, no SGK (tazminat niteliği) - the user can flip each flag per line.
    /// </summary>
    public static ExitPayTaxTreatment DefaultTreatment(ExitPayKind kind) => kind switch
    {
        ExitPayKind.Severance => new ExitPayTaxTreatment(false, false, true),
        ExitPayKind.UnusedLeave or ExitPayKind.FinalMonthWage => new ExitPayTaxTreatment(true, true, true),
        _ => new ExitPayTaxTreatment(false, true, true),
    };

    public static ExitNetResult Calculate(IReadOnlyList<ExitPayInput> lines, decimal priorCumulativeGvBase, PayrollLegislationSnapshot p)
    {
        Guard.Against(priorCumulativeGvBase < 0m, "priorCumulativeGvBase must not be negative.");
        Guard.Against(lines.Any(l => l.Gross < 0m), "A line's gross must not be negative.");

        var gross = lines.Select(l => l.Gross).ToArray();

        var sgkWeights = lines.Select(l => l.Treatment.SgkSubject ? l.Gross : 0m).ToArray();
        var sgkBase = Math.Min(sgkWeights.Sum(), p.SgkCeiling);
        var sgkTotal = Round(sgkBase * (p.SgkEmployeeRate + p.UnemploymentEmployeeRate));
        var sgk = Allocate(sgkTotal, sgkWeights);

        var gvBases = lines.Select((l, i) => l.Treatment.IncomeTaxSubject ? l.Gross - sgk[i] : 0m).ToArray();
        var gvBaseTotal = gvBases.Sum();
        var incomeTaxTotal = Round(
            IncomeTaxMath.TaxUpTo(p.IncomeTaxBrackets, priorCumulativeGvBase + gvBaseTotal)
            - IncomeTaxMath.TaxUpTo(p.IncomeTaxBrackets, priorCumulativeGvBase));
        var incomeTax = Allocate(incomeTaxTotal, gvBases);

        var stamp = lines.Select(l => l.Treatment.StampTaxSubject ? Round(l.Gross * p.StampTaxRate) : 0m).ToArray();

        var results = lines
            .Select((l, i) => new ExitPayLineResult(l.Key, gross[i], sgk[i], incomeTax[i], stamp[i], gross[i] - sgk[i] - incomeTax[i] - stamp[i]))
            .ToList();

        return new ExitNetResult(
            results, gross.Sum(), sgk.Sum(), incomeTax.Sum(), stamp.Sum(), results.Sum(r => r.Net));
    }

    /// <summary>Splits <paramref name="total"/> across the weights in proportion, cent-exact: the rounding remainder goes to the heaviest line so the parts always add up.</summary>
    private static decimal[] Allocate(decimal total, decimal[] weights)
    {
        var parts = new decimal[weights.Length];
        var weightSum = weights.Sum();
        if (total == 0m || weightSum <= 0m)
        {
            return parts;
        }

        for (var i = 0; i < weights.Length; i++)
        {
            parts[i] = Round(total * weights[i] / weightSum);
        }

        var heaviest = Array.IndexOf(weights, weights.Max());
        parts[heaviest] += total - parts.Sum();
        return parts;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
