using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

/// <summary>
/// Hand-written recursive-descent parser. Precedence, low to high: veya/or, ve/and, degil/not, comparison, + -, * /, unary minus.
/// Every parse method returns null after recording <see cref="_error"/>; nothing throws.
/// </summary>
internal sealed class FormulaParser
{
    private readonly List<FormulaToken> _tokens;
    private readonly SortedSet<string> _usedVariables = new(StringComparer.Ordinal);
    private int _index;
    private int _depth;
    private Error? _error;

    private FormulaParser(List<FormulaToken> tokens) => _tokens = tokens;

    public static Result<(FormulaNode Root, IReadOnlyList<string> UsedVariables)> Parse(List<FormulaToken> tokens)
    {
        var parser = new FormulaParser(tokens);
        var root = parser.ParseExpression();
        if (root is not null && parser.Current.Kind != FormulaTokenKind.End)
        {
            return parser.SyntaxErrorAtCurrent();
        }

        return root is null
            ? parser._error!
            : (root, (IReadOnlyList<string>)parser._usedVariables.ToList());
    }

    private FormulaToken Current => _tokens[_index];

    private FormulaToken Previous => _tokens[Math.Max(_index - 1, 0)];

    private FormulaToken Advance() => _tokens[_index < _tokens.Count - 1 ? _index++ : _index];

    private T? Fail<T>(Error error)
        where T : class
    {
        _error ??= error;
        return null;
    }

    private Error SyntaxErrorAtCurrent()
    {
        _error ??= IsDecimalComma(Current)
            ? FormulaErrors.DecimalComma(Current.Position)
            : FormulaErrors.Syntax(Current.Position);
        return _error;
    }

    private bool Enter(FormulaToken at)
    {
        if (++_depth > FormulaLimits.MaxDepth)
        {
            _error ??= FormulaErrors.TooDeep(at.Position);
            return false;
        }

        return true;
    }

    private static bool IsWord(FormulaToken t, string turkish, string english) =>
        t.Kind == FormulaTokenKind.Identifier && (t.Text == turkish || t.Text == english);

    private FormulaNode? ParseExpression()
    {
        if (!Enter(Current))
        {
            return null;
        }

        var node = ParseOr();
        _depth--;
        return node;
    }

    private FormulaNode? ParseOr() =>
        ParseLogical(ParseAnd, "veya", "or", FormulaBinaryOp.Or);

    private FormulaNode? ParseAnd() =>
        ParseLogical(ParseNot, "ve", "and", FormulaBinaryOp.And);

    private FormulaNode? ParseLogical(Func<FormulaNode?> operand, string turkish, string english, FormulaBinaryOp op)
    {
        var left = operand();
        while (left is not null && IsWord(Current, turkish, english))
        {
            var token = Advance();
            var right = operand();
            left = right is null ? null : new BinaryNode(token.Position, op, left, right);
        }

        return left;
    }

    private FormulaNode? ParseNot()
    {
        if (!IsWord(Current, "degil", "not"))
        {
            return ParseComparison();
        }

        var token = Advance();
        if (!Enter(token))
        {
            return null;
        }

        var operand = ParseNot();
        _depth--;
        return operand is null ? null : new NotNode(token.Position, operand);
    }

    private FormulaNode? ParseComparison()
    {
        var left = ParseAdditive();
        while (left is not null && ComparisonOp(Current.Kind) is { } op)
        {
            var token = Advance();
            var right = ParseAdditive();
            left = right is null ? null : new BinaryNode(token.Position, op, left, right);
        }

        return left;
    }

    private static FormulaBinaryOp? ComparisonOp(FormulaTokenKind kind) => kind switch
    {
        FormulaTokenKind.Less => FormulaBinaryOp.Less,
        FormulaTokenKind.LessOrEqual => FormulaBinaryOp.LessOrEqual,
        FormulaTokenKind.Greater => FormulaBinaryOp.Greater,
        FormulaTokenKind.GreaterOrEqual => FormulaBinaryOp.GreaterOrEqual,
        FormulaTokenKind.Equal => FormulaBinaryOp.Equal,
        FormulaTokenKind.NotEqual => FormulaBinaryOp.NotEqual,
        _ => null,
    };

    private FormulaNode? ParseAdditive()
    {
        var left = ParseTerm();
        while (left is not null && Current.Kind is FormulaTokenKind.Plus or FormulaTokenKind.Minus)
        {
            var token = Advance();
            var right = ParseTerm();
            var op = token.Kind == FormulaTokenKind.Plus ? FormulaBinaryOp.Add : FormulaBinaryOp.Subtract;
            left = right is null ? null : new BinaryNode(token.Position, op, left, right);
        }

        return left;
    }

    private FormulaNode? ParseTerm()
    {
        var left = ParseUnary();
        while (left is not null && Current.Kind is FormulaTokenKind.Star or FormulaTokenKind.Slash)
        {
            var token = Advance();
            var right = ParseUnary();
            var op = token.Kind == FormulaTokenKind.Star ? FormulaBinaryOp.Multiply : FormulaBinaryOp.Divide;
            left = right is null ? null : new BinaryNode(token.Position, op, left, right);
        }

        return left;
    }

    private FormulaNode? ParseUnary()
    {
        if (Current.Kind != FormulaTokenKind.Minus)
        {
            return ParsePrimary();
        }

        var token = Advance();
        if (!Enter(token))
        {
            return null;
        }

        var operand = ParseUnary();
        _depth--;
        return operand is null ? null : new NegateNode(token.Position, operand);
    }

    private FormulaNode? ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case FormulaTokenKind.Number:
                Advance();
                return new NumberNode(token.Position, token.Number);
            case FormulaTokenKind.LeftParen:
                Advance();
                return ParseParenthesized();
            case FormulaTokenKind.Identifier:
                Advance();
                return Current.Kind == FormulaTokenKind.LeftParen ? ParseCall(token) : ParseVariable(token);
            default:
                return Fail<FormulaNode>(FormulaErrors.Syntax(token.Position));
        }
    }

    private FormulaNode? ParseParenthesized()
    {
        var inner = ParseExpression();
        if (inner is null)
        {
            return null;
        }

        return Expect(FormulaTokenKind.RightParen) ? inner : null;
    }

    private FormulaNode? ParseVariable(FormulaToken token)
    {
        if (!PayItemVariableNames.IsAllowed(token.Text))
        {
            return Fail<FormulaNode>(FormulaErrors.UnknownVariable(token.Position, token.Text));
        }

        _usedVariables.Add(token.Text);
        return new VariableNode(token.Position, token.Text);
    }

    private FormulaNode? ParseCall(FormulaToken name)
    {
        var spec = FormulaFunctions.Find(name.Text);
        if (spec is null)
        {
            return Fail<FormulaNode>(FormulaErrors.UnknownFunction(name.Position, name.Text));
        }

        Advance(); // (
        var arguments = ParseArguments();
        if (arguments is null)
        {
            return null;
        }

        var info = spec.Info;
        if (arguments.Count < info.MinArgs || (info.MaxArgs is { } max && arguments.Count > max))
        {
            return Fail<FormulaNode>(FormulaErrors.ArgumentCount(name.Position, info.Name, info.MaxArgs));
        }

        return new CallNode(name.Position, spec, arguments);
    }

    private List<FormulaNode>? ParseArguments()
    {
        var arguments = new List<FormulaNode>();
        if (Current.Kind == FormulaTokenKind.RightParen)
        {
            Advance();
            return arguments;
        }

        while (true)
        {
            var argument = ParseExpression();
            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);
            if (Current.Kind is FormulaTokenKind.Comma or FormulaTokenKind.Semicolon)
            {
                Advance();
                continue;
            }

            return Expect(FormulaTokenKind.RightParen) ? arguments : null;
        }
    }

    private bool Expect(FormulaTokenKind kind)
    {
        if (Current.Kind == kind)
        {
            Advance();
            return true;
        }

        SyntaxErrorAtCurrent();
        return false;
    }

    // "1,5" written outside an argument list (where a comma is not a separator): number, adjacent comma, adjacent number.
    private bool IsDecimalComma(FormulaToken comma) =>
        comma.Kind == FormulaTokenKind.Comma
        && _index > 0
        && Previous.Kind == FormulaTokenKind.Number
        && Previous.End == comma.Position
        && _index + 1 < _tokens.Count
        && _tokens[_index + 1].Kind == FormulaTokenKind.Number
        && _tokens[_index + 1].Position == comma.Position + 1;
}
