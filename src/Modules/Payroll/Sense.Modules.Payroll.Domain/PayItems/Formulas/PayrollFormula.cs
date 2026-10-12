using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

/// <summary>
/// A parsed, validated pay-item formula. Pure and culture-independent: decimal arithmetic only, decimal point is ".".
/// <see cref="Parse"/> and <see cref="Evaluate"/> return a <see cref="Result{TValue}"/> and never throw; error positions are 1-based.
/// </summary>
public sealed class PayrollFormula
{
    private readonly FormulaNode _root;

    private PayrollFormula(string text, FormulaNode root, IReadOnlyList<string> usedVariables)
    {
        Text = text;
        _root = root;
        UsedVariables = usedVariables;
    }

    /// <summary>The user's original text (stored in the calculation trace).</summary>
    public string Text { get; }

    /// <summary>Distinct variable names the formula references, sorted ordinally.</summary>
    public IReadOnlyList<string> UsedVariables { get; }

    /// <summary>Function catalog for the editor: Turkish names with English aliases.</summary>
    public static IReadOnlyList<FormulaFunctionInfo> Functions { get; } = FormulaFunctions.All.Select(f => f.Info).ToList();

    public static Result<PayrollFormula> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return FormulaErrors.Empty();
        }

        if (text.Length > FormulaLimits.MaxTextLength)
        {
            return FormulaErrors.TooLong(FormulaLimits.MaxTextLength + 1, FormulaLimits.MaxTextLength);
        }

        var tokens = FormulaLexer.Tokenize(text);
        if (tokens.IsFailure)
        {
            return tokens.Error;
        }

        var parsed = FormulaParser.Parse(tokens.Value);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        return new PayrollFormula(text, parsed.Value.Root, parsed.Value.UsedVariables);
    }

    /// <summary>Evaluates with the given variable values (lower-case names). A used variable missing from the map is an unknown-variable error.</summary>
    public Result<decimal> Evaluate(IReadOnlyDictionary<string, decimal> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return _root.Evaluate(new FormulaScope(variables));
    }
}
