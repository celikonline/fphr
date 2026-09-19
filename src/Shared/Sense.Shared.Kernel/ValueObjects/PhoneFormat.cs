namespace Sense.Shared.Kernel.ValueObjects;

/// <summary>
/// Lenient input-format check for free-typed phone numbers (companion of <see cref="PhoneNormalizer"/>, which silently
/// drops everything that is not a digit - so "abc" used to be accepted and vanish). Accepts what people really type:
/// digits with spaces, dashes, dots, slashes and parentheses, and an optional leading "+". Rejects letters and other
/// junk. Empty is valid (the field is optional; requiredness is a separate rule).
/// </summary>
public static class PhoneFormat
{
    /// <summary>Shortest accepted digit count (allows a plain internal extension).</summary>
    public const int MinDigits = 4;

    /// <summary>Longest accepted digit count (E.164 caps at 15; a few spare for an appended extension).</summary>
    public const int MaxDigits = 20;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        var digits = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c == '+' && i == 0)
            {
                // leading plus: country prefix
            }
            else if (c is not (' ' or '-' or '.' or '/' or '(' or ')'))
            {
                return false;
            }
        }

        return digits is >= MinDigits and <= MaxDigits;
    }
}
