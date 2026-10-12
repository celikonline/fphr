using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

/// <summary>Builds formula errors. Positions are 1-based character offsets into the formula text.</summary>
internal static class FormulaErrors
{
    public static Error Empty() => Error.Validation(PayrollDomainErrors.FormulaEmpty);

    public static Error Syntax(int position) => Error.Validation(PayrollDomainErrors.FormulaSyntax, ("position", position));

    public static Error UnknownVariable(int position, string name) =>
        Error.Validation(PayrollDomainErrors.FormulaUnknownVariable, ("position", position), ("name", name));

    public static Error UnknownFunction(int position, string name) =>
        Error.Validation(PayrollDomainErrors.FormulaUnknownFunction, ("position", position), ("name", name));

    public static Error ArgumentCount(int position, string name, int? max) =>
        max is { } m
            ? Error.Validation(PayrollDomainErrors.FormulaArgumentCount, ("position", position), ("name", name), ("max", m))
            : Error.Validation(PayrollDomainErrors.FormulaArgumentCount, ("position", position), ("name", name));

    public static Error TooLong(int position, int max) =>
        Error.Validation(PayrollDomainErrors.FormulaTooLong, ("position", position), ("max", max));

    public static Error TooDeep(int position) =>
        Error.Validation(PayrollDomainErrors.FormulaTooDeep, ("position", position), ("max", FormulaLimits.MaxDepth));

    public static Error DivisionByZero(int position) => Error.Rule(PayrollDomainErrors.FormulaDivisionByZero, ("position", position));

    public static Error Overflow(int position) => Error.Rule(PayrollDomainErrors.FormulaOverflow, ("position", position));

    public static Error DecimalComma(int position) => Error.Validation(PayrollDomainErrors.FormulaDecimalComma, ("position", position));
}
