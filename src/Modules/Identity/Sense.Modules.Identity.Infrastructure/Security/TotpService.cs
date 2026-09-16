using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sense.Modules.Identity.Application;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// RFC 6238 (TOTP) / RFC 4226 (HOTP) — BCL `HMACSHA1` ile, ek NuGet paketi yok (PO kararı, Faz 3 Tur 3). 30 sn adım,
/// 6 haneli kod, ±1 pencere (önceki/sonraki adım) saat kaymasına tolerans tanır.
/// </summary>
public sealed class TotpService(TimeProvider clock) : ITotpService
{
    private const int SecretBytes = 20;
    private const int StepSeconds = 30;
    private const int Digits = 6;
    private const int ToleranceSteps = 1;

    public string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    public string BuildOtpAuthUri(string secretBase32, string accountLabel, string issuer)
    {
        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(accountLabel);
        return string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}?secret={1}&issuer={2}&algorithm=SHA1&digits={3}&period={4}",
            label, secretBase32, Uri.EscapeDataString(issuer), Digits, StepSeconds);
    }

    public bool ValidateCode(string secretBase32, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = Base32.Decode(secretBase32);
        var counter = clock.GetUtcNow().ToUnixTimeSeconds() / StepSeconds;

        for (var window = -ToleranceSteps; window <= ToleranceSteps; window++)
        {
            if (ComputeCode(key, counter + window) == code)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>RFC 4226 §5.3: HMAC-SHA1(key, counter) → dinamik kısaltma → mod 10^Digits, sıfır dolgulu.</summary>
    private static string ComputeCode(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        // RFC 6238/4226 HMAC-SHA1 tanımlar; authenticator uygulamalarının (Google/Microsoft Authenticator) varsayılanı budur.
        // HOTP'de SHA1 çakışma direnci değil HMAC PRF özelliği kullanıldığı için güvenlik zafiyeti değildir.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(key, counterBytes);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }
}

/// <summary>RFC 4648 Base32 (dolgusuz) — TOTP secret'ları için. Genel amaçlı değil, yalnız TotpService kullanır.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsInBuffer = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                sb.Append(Alphabet[(buffer >> bitsInBuffer) & 0x1F]);
            }
        }

        if (bitsInBuffer > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bitsInBuffer)) & 0x1F]);
        }

        return sb.ToString();
    }

    public static byte[] Decode(string base32)
    {
        var bytes = new List<byte>(base32.Length * 5 / 8);
        int buffer = 0, bitsInBuffer = 0;
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(c);
            if (index < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | index;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((buffer >> bitsInBuffer) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
