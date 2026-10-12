using System.Globalization;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.PayItems.Formulas;

internal enum FormulaTokenKind
{
    Number,
    Identifier,
    Plus,
    Minus,
    Star,
    Slash,
    LeftParen,
    RightParen,
    Comma,
    Semicolon,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
    End,
}

/// <param name="Text">Identifiers are lower-cased (ordinal-invariant, so Turkish-I cannot change a name).</param>
/// <param name="Position">1-based offset of the first character.</param>
internal readonly record struct FormulaToken(FormulaTokenKind Kind, string Text, int Position, decimal Number = 0m)
{
    public int End => Position + Text.Length;
}

/// <summary>Splits formula text into tokens; never throws.</summary>
internal static class FormulaLexer
{
    public static Result<List<FormulaToken>> Tokenize(string text)
    {
        var tokens = new List<FormulaToken>();
        var index = 0;
        while (true)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (tokens.Count >= FormulaLimits.MaxTokens)
            {
                return FormulaErrors.TooLong(index + 1, FormulaLimits.MaxTokens);
            }

            if (index >= text.Length)
            {
                tokens.Add(new FormulaToken(FormulaTokenKind.End, string.Empty, text.Length + 1));
                return tokens;
            }

            var read = ReadToken(text, ref index);
            if (read.IsFailure)
            {
                return read.Error;
            }

            tokens.Add(read.Value);
        }
    }

    private static Result<FormulaToken> ReadToken(string text, ref int index)
    {
        var c = text[index];
        if (IsAsciiDigit(c))
        {
            return ReadNumber(text, ref index);
        }

        if (IsIdentifierStart(c))
        {
            return ReadIdentifier(text, ref index);
        }

        return ReadOperator(text, ref index);
    }

    private static Result<FormulaToken> ReadNumber(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (index + 1 < text.Length && text[index] == '.' && IsAsciiDigit(text[index + 1]))
        {
            index++;
            while (index < text.Length && IsAsciiDigit(text[index]))
            {
                index++;
            }
        }

        var raw = text[start..index];
        if (raw.Length > FormulaLimits.MaxNumberLength)
        {
            return FormulaErrors.TooLong(start + 1, FormulaLimits.MaxNumberLength);
        }

        if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value > FormulaLimits.MaxMagnitude)
        {
            return FormulaErrors.Overflow(start + 1);
        }

        return new FormulaToken(FormulaTokenKind.Number, raw, start + 1, value);
    }

    private static Result<FormulaToken> ReadIdentifier(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && (IsIdentifierStart(text[index]) || IsAsciiDigit(text[index])))
        {
            index++;
        }

        if (index - start > FormulaLimits.MaxIdentifierLength)
        {
            return FormulaErrors.TooLong(start + 1, FormulaLimits.MaxIdentifierLength);
        }

        return new FormulaToken(FormulaTokenKind.Identifier, text[start..index].ToLowerInvariant(), start + 1);
    }

    private static Result<FormulaToken> ReadOperator(string text, ref int index)
    {
        var start = index;
        var c = text[index];
        var next = index + 1 < text.Length ? text[index + 1] : '\0';
        var (kind, length) = c switch
        {
            '+' => (FormulaTokenKind.Plus, 1),
            '-' => (FormulaTokenKind.Minus, 1),
            '*' => (FormulaTokenKind.Star, 1),
            '/' => (FormulaTokenKind.Slash, 1),
            '(' => (FormulaTokenKind.LeftParen, 1),
            ')' => (FormulaTokenKind.RightParen, 1),
            ',' => (FormulaTokenKind.Comma, 1),
            ';' => (FormulaTokenKind.Semicolon, 1),
            '=' => (FormulaTokenKind.Equal, 1),
            '<' when next == '=' => (FormulaTokenKind.LessOrEqual, 2),
            '<' => (FormulaTokenKind.Less, 1),
            '>' when next == '=' => (FormulaTokenKind.GreaterOrEqual, 2),
            '>' => (FormulaTokenKind.Greater, 1),
            '!' when next == '=' => (FormulaTokenKind.NotEqual, 2),
            _ => (FormulaTokenKind.End, 0),
        };

        if (length == 0)
        {
            return FormulaErrors.Syntax(start + 1);
        }

        index += length;
        return new FormulaToken(kind, text.Substring(start, length), start + 1);
    }

    // ASCII only: char.IsDigit/IsLetter accept other scripts, which decimal.TryParse and the variable list do not.
    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsIdentifierStart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';
}
