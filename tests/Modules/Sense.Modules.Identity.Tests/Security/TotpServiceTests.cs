using System.Text;
using Sense.Modules.Identity.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Security;

/// <summary>
/// RFC 6238 (TOTP) / RFC 4226 (HOTP) test vektörleri. RFC 4226 Appendix D'nin ASCII secret'ı ("12345678901234567890",
/// 20 byte) Base32'ye kodlanıp kullanılır; TOTP = HOTP(K, floor(unixTime/30)) olduğundan unixTime=0 → counter=0,
/// unixTime=59 → counter=1 RFC 4226'nın counter=0/1 vektörleriyle (755224 / 287082) birebir örtüşür — RFC 6238'in
/// kendi 8 haneli örnek tablosunun (94287082, t=59) son 6 hanesiyle de tutarlıdır.
/// </summary>
public sealed class TotpServiceTests
{
    // RFC 4226 Appendix D ASCII secret, Base32 kodlu.
    private static readonly string Rfc4226SecretBase32 = Base32EncodeAscii("12345678901234567890");

    [Fact]
    public void ValidateCode_MatchesRfc4226Vector_ForCounterZero()
    {
        var totp = new TotpService(FixedClock(DateTimeOffset.FromUnixTimeSeconds(0)));

        totp.ValidateCode(Rfc4226SecretBase32, "755224").ShouldBeTrue();
    }

    [Fact]
    public void ValidateCode_MatchesRfc6238Vector_At59Seconds()
    {
        // RFC 6238 Appendix B, T=59s (counter=1); 8 haneli referans "94287082"in son 6 hanesi = "287082" (RFC 4226 counter=1 ile aynı).
        var totp = new TotpService(FixedClock(DateTimeOffset.FromUnixTimeSeconds(59)));

        totp.ValidateCode(Rfc4226SecretBase32, "287082").ShouldBeTrue();
    }

    [Fact]
    public void ValidateCode_WrongCode_ReturnsFalse()
    {
        var totp = new TotpService(FixedClock(DateTimeOffset.FromUnixTimeSeconds(0)));

        totp.ValidateCode(Rfc4226SecretBase32, "000000").ShouldBeFalse();
    }

    [Fact]
    public void ValidateCode_NonNumericOrWrongLength_ReturnsFalse()
    {
        var totp = new TotpService(FixedClock(DateTimeOffset.FromUnixTimeSeconds(0)));

        totp.ValidateCode(Rfc4226SecretBase32, "abcdef").ShouldBeFalse();
        totp.ValidateCode(Rfc4226SecretBase32, "12345").ShouldBeFalse();
    }

    [Fact]
    public void ValidateCode_OneStepDrift_IsToleratedWithinWindow()
    {
        // Sunucu saati counter=2'de (t=60..89); counter=1 (t=59) kodu ±1 pencere toleransıyla hâlâ kabul edilir.
        var totp = new TotpService(FixedClock(DateTimeOffset.FromUnixTimeSeconds(65)));

        totp.ValidateCode(Rfc4226SecretBase32, "287082").ShouldBeTrue();
    }

    [Fact]
    public void ValidateCode_TwoStepDrift_IsRejected()
    {
        // counter=0 (t=0) kodu, sunucu counter=3'te (t=90..119) iken pencere dışında kalır (±1 = counter 2..4).
        var totp = new TotpService(FixedClock(DateTimeOffset.FromUnixTimeSeconds(95)));

        totp.ValidateCode(Rfc4226SecretBase32, "755224").ShouldBeFalse();
    }

    [Fact]
    public void GenerateSecret_ReturnsBase32Alphabet_AndIsUnpredictable()
    {
        var totp = new TotpService(FixedClock(DateTimeOffset.UtcNow));

        var a = totp.GenerateSecret();
        var b = totp.GenerateSecret();

        a.ShouldNotBe(b);
        a.ShouldAllBe(c => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".Contains(c));
    }

    [Fact]
    public void BuildOtpAuthUri_ContainsAccountAndIssuer()
    {
        var totp = new TotpService(FixedClock(DateTimeOffset.UtcNow));

        var uri = totp.BuildOtpAuthUri("JBSWY3DPEHPK3PXP", "kerem@acme.test", "SenseIK");

        uri.ShouldStartWith("otpauth://totp/");
        uri.ShouldContain("secret=JBSWY3DPEHPK3PXP");
        uri.ShouldContain("issuer=SenseIK");
        uri.ShouldContain(Uri.EscapeDataString("kerem@acme.test"));
    }

    private static TimeProvider FixedClock(DateTimeOffset now) => new FixedTimeProvider(now);

    private static string Base32EncodeAscii(string ascii) => Base32EncodeBytes(Encoding.ASCII.GetBytes(ascii));

    // RFC 4648 Base32 (no padding) — mirrors Security/TotpService.cs's internal Base32.Encode, duplicated here
    // because that helper is `internal` to the Infrastructure assembly and not worth exposing solely for a test vector.
    private static string Base32EncodeBytes(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsInBuffer = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                sb.Append(alphabet[(buffer >> bitsInBuffer) & 0x1F]);
            }
        }

        if (bitsInBuffer > 0)
        {
            sb.Append(alphabet[(buffer << (5 - bitsInBuffer)) & 0x1F]);
        }

        return sb.ToString();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
