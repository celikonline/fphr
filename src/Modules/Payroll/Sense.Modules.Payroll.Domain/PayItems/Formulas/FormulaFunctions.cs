namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

internal enum FormulaFunction
{
    Min,
    Max,
    Round,
    Floor,
    Ceiling,
    Abs,
    If,
}

/// <summary>A formula function as shown in the catalog: Turkish primary name plus English aliases. <paramref name="MaxArgs"/> null = unbounded.</summary>
public sealed record FormulaFunctionInfo(string Name, IReadOnlyList<string> Aliases, int MinArgs, int? MaxArgs);

internal sealed record FormulaFunctionSpec(FormulaFunction Function, FormulaFunctionInfo Info);

internal static class FormulaFunctions
{
    public static readonly IReadOnlyList<FormulaFunctionSpec> All =
    [
        new(FormulaFunction.Min, new("min", [], 2, null)),
        new(FormulaFunction.Max, new("max", [], 2, null)),
        new(FormulaFunction.Round, new("yuvarla", ["round"], 1, 2)),
        new(FormulaFunction.Floor, new("asagi", ["floor"], 1, 1)),
        new(FormulaFunction.Ceiling, new("yukari", ["ceil"], 1, 1)),
        new(FormulaFunction.Abs, new("mutlak", ["abs"], 1, 1)),
        new(FormulaFunction.If, new("eger", ["if"], 3, 3)),
    ];

    private static readonly Dictionary<string, FormulaFunctionSpec> ByName = BuildIndex();

    public static FormulaFunctionSpec? Find(string lowerName) => ByName.GetValueOrDefault(lowerName);

    private static Dictionary<string, FormulaFunctionSpec> BuildIndex()
    {
        var index = new Dictionary<string, FormulaFunctionSpec>(StringComparer.Ordinal);
        foreach (var spec in All)
        {
            index[spec.Info.Name] = spec;
            foreach (var alias in spec.Info.Aliases)
            {
                index[alias] = spec;
            }
        }

        return index;
    }
}
