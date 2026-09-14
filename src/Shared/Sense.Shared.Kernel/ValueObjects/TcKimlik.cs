using Sense.Shared.Kernel.Domain;

namespace Sense.Shared.Kernel.ValueObjects;

/// <summary>T.C. Kimlik Numarası (11 hane, kontrol basamağı algoritması) veya 99 ile başlayan Yabancı Kimlik Numarası.</summary>
public sealed class TcKimlik : ValueObject
{
    private TcKimlik(string value) => Value = value;

    public string Value { get; }

    public bool IsForeign => Value.StartsWith("99", StringComparison.Ordinal);

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var s = value.Trim();
        if (s.Length != 11 || s[0] == '0' || !s.All(char.IsAsciiDigit))
        {
            return false;
        }

        // Yabancı kimlik numaraları 99 ile başlar; kontrol basamağı algoritması aynıdır.
        var d = s.Select(c => c - '0').ToArray();
        var oddSum = d[0] + d[2] + d[4] + d[6] + d[8];
        var evenSum = d[1] + d[3] + d[5] + d[7];
        var tenth = ((oddSum * 7) - evenSum) % 10;
        if (tenth < 0)
        {
            tenth += 10;
        }

        if (d[9] != tenth)
        {
            return false;
        }

        var eleventh = d.Take(10).Sum() % 10;
        return d[10] == eleventh;
    }

    public static TcKimlik Of(string value)
    {
        Guard.Against(!IsValid(value), KernelMessages.InvalidNationalId);
        return new TcKimlik(value.Trim());
    }

    public static bool TryCreate(string? value, out TcKimlik? result)
    {
        result = IsValid(value) ? new TcKimlik(value!.Trim()) : null;
        return result is not null;
    }

    /// <summary>Ekranlarda gösterim: 123******01.</summary>
    public string Masked => string.Concat(Value.AsSpan(0, 3), "******", Value.AsSpan(9, 2));

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
