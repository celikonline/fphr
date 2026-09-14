using System.Numerics;
using Sense.Shared.Kernel.Domain;

namespace Sense.Shared.Kernel.ValueObjects;

/// <summary>IBAN (ISO 13616, mod-97). Türkiye IBAN'ı 26 karakter, TR ile başlar.</summary>
public sealed class Iban : ValueObject
{
    private Iban(string value) => Value = value;

    /// <summary>Boşluksuz, büyük harf normalize değer.</summary>
    public string Value { get; }

    public string CountryCode => Value[..2];

    public bool IsTurkish => CountryCode == "TR";

    /// <summary>TR IBAN'da banka kodu (5 hane).</summary>
    public string? BankCode => IsTurkish ? Value.Substring(4, 5) : null;

    public static string Normalize(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var s = Normalize(value);
        if (s.Length is < 15 or > 34 || !s.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(s[0]) || !char.IsAsciiLetterUpper(s[1]) || !char.IsAsciiDigit(s[2]) || !char.IsAsciiDigit(s[3]))
        {
            return false;
        }

        if (s.StartsWith("TR", StringComparison.Ordinal) && s.Length != 26)
        {
            return false;
        }

        var rearranged = string.Concat(s.AsSpan(4), s.AsSpan(0, 4));
        var numeric = new System.Text.StringBuilder(rearranged.Length * 2);
        foreach (var c in rearranged)
        {
            numeric.Append(char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return BigInteger.Parse(numeric.ToString(), System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }

    public static Iban Of(string value)
    {
        Guard.Against(!IsValid(value), KernelMessages.InvalidIban);
        return new Iban(Normalize(value));
    }

    public static bool TryCreate(string? value, out Iban? result)
    {
        result = IsValid(value) ? new Iban(Normalize(value!)) : null;
        return result is not null;
    }

    /// <summary>TR12 3456 7890 1234 5678 9012 34 biçimi.</summary>
    public string Formatted => string.Join(' ', Enumerable.Range(0, (Value.Length + 3) / 4).Select(i => Value.Substring(i * 4, Math.Min(4, Value.Length - (i * 4)))));

    public string Masked => string.Concat(Value.AsSpan(0, 4), new string('*', Value.Length - 8), Value.AsSpan(Value.Length - 4));

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
