namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

/// <summary>Named safety limits of the pay-item formula language (plan: "Formül dili"). A formula is user input, so everything is bounded.</summary>
public static class FormulaLimits
{
    public const int MaxTextLength = 500;
    public const int MaxTokens = 200;
    public const int MaxDepth = 20;
    public const int MaxIdentifierLength = 40;
    public const int MaxNumberLength = 20;

    /// <summary>Any literal, intermediate or final value whose magnitude exceeds this is an overflow error (also keeps decimal products far from decimal.MaxValue).</summary>
    public const decimal MaxMagnitude = 1_000_000_000m;

    public const int MinRoundDigits = 0;
    public const int MaxRoundDigits = 4;
}
