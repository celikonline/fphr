using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

/// <summary>Variable values for one evaluation, keyed by lower-case name (see <see cref="PayItemVariableNames"/>).</summary>
internal readonly record struct FormulaScope(IReadOnlyDictionary<string, decimal> Variables);

/// <summary>Syntax tree node. Evaluation returns a <see cref="Result{TValue}"/>; booleans are 1 / 0.</summary>
internal abstract record FormulaNode(int Position)
{
    public abstract Result<decimal> Evaluate(FormulaScope scope);

    protected static Result<decimal> Checked(decimal value, int position) =>
        Math.Abs(value) > FormulaLimits.MaxMagnitude ? FormulaErrors.Overflow(position) : value;

    protected static decimal FromBool(bool value) => value ? 1m : 0m;
}

internal sealed record NumberNode(int Position, decimal Value) : FormulaNode(Position)
{
    public override Result<decimal> Evaluate(FormulaScope scope) => Value;
}

internal sealed record VariableNode(int Position, string Name) : FormulaNode(Position)
{
    public override Result<decimal> Evaluate(FormulaScope scope) =>
        scope.Variables.TryGetValue(Name, out var value)
            ? Checked(value, Position)
            : FormulaErrors.UnknownVariable(Position, Name);
}

internal sealed record NegateNode(int Position, FormulaNode Operand) : FormulaNode(Position)
{
    public override Result<decimal> Evaluate(FormulaScope scope) => Operand.Evaluate(scope).Map(v => -v);
}

internal sealed record NotNode(int Position, FormulaNode Operand) : FormulaNode(Position)
{
    public override Result<decimal> Evaluate(FormulaScope scope) => Operand.Evaluate(scope).Map(v => FromBool(v == 0m));
}

internal enum FormulaBinaryOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
    And,
    Or,
}

internal sealed record BinaryNode(int Position, FormulaBinaryOp Op, FormulaNode Left, FormulaNode Right) : FormulaNode(Position)
{
    public override Result<decimal> Evaluate(FormulaScope scope)
    {
        var left = Left.Evaluate(scope);
        if (left.IsFailure)
        {
            return left;
        }

        // Logical operators short-circuit so "b != 0 ve a / b > 1" never divides by zero.
        if (Op == FormulaBinaryOp.And && left.Value == 0m)
        {
            return 0m;
        }

        if (Op == FormulaBinaryOp.Or && left.Value != 0m)
        {
            return 1m;
        }

        var right = Right.Evaluate(scope);
        return right.IsFailure ? right : Apply(left.Value, right.Value);
    }

    private Result<decimal> Apply(decimal l, decimal r)
    {
        try
        {
            return Op switch
            {
                FormulaBinaryOp.Add => Checked(l + r, Position),
                FormulaBinaryOp.Subtract => Checked(l - r, Position),
                FormulaBinaryOp.Multiply => Checked(l * r, Position),
                FormulaBinaryOp.Divide => r == 0m ? FormulaErrors.DivisionByZero(Position) : Checked(l / r, Position),
                FormulaBinaryOp.Less => FromBool(l < r),
                FormulaBinaryOp.LessOrEqual => FromBool(l <= r),
                FormulaBinaryOp.Greater => FromBool(l > r),
                FormulaBinaryOp.GreaterOrEqual => FromBool(l >= r),
                FormulaBinaryOp.Equal => FromBool(l == r),
                FormulaBinaryOp.NotEqual => FromBool(l != r),
                FormulaBinaryOp.And or FormulaBinaryOp.Or => FromBool(r != 0m),
                _ => FormulaErrors.Syntax(Position),
            };
        }
        catch (OverflowException)
        {
            return FormulaErrors.Overflow(Position);
        }
    }
}

internal sealed record CallNode(int Position, FormulaFunctionSpec Spec, IReadOnlyList<FormulaNode> Arguments) : FormulaNode(Position)
{
    public override Result<decimal> Evaluate(FormulaScope scope)
    {
        // Only the chosen branch of eger/if is evaluated (the other may divide by zero).
        if (Spec.Function == FormulaFunction.If)
        {
            var condition = Arguments[0].Evaluate(scope);
            if (condition.IsFailure)
            {
                return condition;
            }

            return Arguments[condition.Value != 0m ? 1 : 2].Evaluate(scope);
        }

        var values = new decimal[Arguments.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var argument = Arguments[i].Evaluate(scope);
            if (argument.IsFailure)
            {
                return argument;
            }

            values[i] = argument.Value;
        }

        return Apply(values);
    }

    private Result<decimal> Apply(decimal[] v) => Spec.Function switch
    {
        FormulaFunction.Min => v.Min(),
        FormulaFunction.Max => v.Max(),
        FormulaFunction.Floor => Math.Floor(v[0]),
        FormulaFunction.Ceiling => Math.Ceiling(v[0]),
        FormulaFunction.Abs => Math.Abs(v[0]),
        FormulaFunction.Round => Round(v),
        _ => FormulaErrors.Syntax(Position),
    };

    // Half away from zero (payroll convention), not banker's rounding.
    private Result<decimal> Round(decimal[] v)
    {
        var digits = v.Length > 1 ? v[1] : 0m;
        if (digits != decimal.Truncate(digits) || digits < FormulaLimits.MinRoundDigits || digits > FormulaLimits.MaxRoundDigits)
        {
            return FormulaErrors.Syntax(Position);
        }

        return Math.Round(v[0], (int)digits, MidpointRounding.AwayFromZero);
    }
}
