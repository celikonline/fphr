using System.Numerics;
using Sense.Shared.Kernel.Domain;

namespace Sense.Shared.Kernel.ValueObjects;

/// <summary>IBAN (ISO 13616, mod-97). Türkiye IBAN'ı 26 karakter, TR ile başlar.</summary>
public sealed class Iban : ValueObject
{
    public const int MinLength = 15;
    public const int MaxLength = 34;
    private const int GroupSize = 4;
    private const int VisibleTailLength = 4;
    private const char MaskChar = '*';

    /// <summary>Ülkeye özgü sabit uzunluklar (ISO 13616 kaydı); listede olmayan ülke yalnız genel 15–34 kuralına tabidir.</summary>
    private static readonly Dictionary<string, int> CountryLengths = new(StringComparer.Ordinal) { ["TR"] = 26 };

    /// <summary>BBAN'ı yalnız rakamlardan oluşan ülkeler (TR: 5 banka + 1 rezerv + 16 hesap hanesi).</summary>
    private static readonly HashSet<string> NumericBbanCountries = new(StringComparer.Ordinal) { "TR" };

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
        if (s.Length is < MinLength or > MaxLength || !s.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(s[0]) || !char.IsAsciiLetterUpper(s[1]) || !char.IsAsciiDigit(s[2]) || !char.IsAsciiDigit(s[3]))
        {
            return false;
        }

        var country = s[..2];
        if (CountryLengths.TryGetValue(country, out var length) && s.Length != length)
        {
            return false;
        }

        if (NumericBbanCountries.Contains(country) && !s.Skip(4).All(char.IsAsciiDigit))
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
    public string Formatted => Group(Value);

    public string Masked => string.Concat(Value.AsSpan(0, 4), new string('*', Value.Length - 8), Value.AsSpan(Value.Length - 4));

    /// <summary>Ülke kodu ve son 4 hane dışındaki her şey gizli, 4'lü gruplu: "TR** **** **** **** **** **12 34" (kontrol haneleri de gizlenir).</summary>
    public string MaskedFormatted => Group(string.Concat(
        Value.AsSpan(0, 2),
        new string(MaskChar, Value.Length - 2 - VisibleTailLength),
        Value.AsSpan(Value.Length - VisibleTailLength)));

    public override string ToString() => Value;

    private static string Group(string value) =>
        string.Join(' ', Enumerable.Range(0, (value.Length + GroupSize - 1) / GroupSize)
            .Select(i => value.Substring(i * GroupSize, Math.Min(GroupSize, value.Length - (i * GroupSize)))));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
