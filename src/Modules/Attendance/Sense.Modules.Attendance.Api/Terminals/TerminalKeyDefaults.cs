namespace Sense.Modules.Attendance.Api.Terminals;

/// <summary>Terminal okutma ucunun kimlik doğrulama sözleşmesi (Z5.1).</summary>
public static class TerminalKeyDefaults
{
    /// <summary>Kendi kimlik doğrulama şeması: JWT'den ayrı, varsayılan şema değil - yalnız okutma ucunda istenir.</summary>
    public const string AuthenticationScheme = "TerminalKey";

    public const string HeaderName = "X-Terminal-Key";

    /// <summary>Hız sınırı politikası (bkz. <see cref="TerminalIngestRateLimiting"/>).</summary>
    public const string RateLimitPolicy = "terminal-ingest";

    /// <summary>Terminal kimliğinin talepleri; kullanıcı talepleriyle (sub/tid) karışmasın diye ayrı adlar.</summary>
    public const string TerminalIdClaim = "sense:terminal_id";

    public const string TenantIdClaim = "sense:terminal_tenant";

    public const string DirectionModeClaim = "sense:terminal_direction";

    public const string LocationIdClaim = "sense:terminal_location";

    public const string CodeClaim = "sense:terminal_code";

    /// <summary>Denetim kaydındaki aktör adı öneki: "terminal:{kod}".</summary>
    public const string AuditActorPrefix = "terminal:";

    /// <summary>Başarısızlık iletisi sabittir: anahtar, kiracı ya da terminal bilgisi içermez.</summary>
    public const string InvalidKeyMessage = "Invalid terminal key.";

    /// <summary>Okutma isteği gövde sınırı (500 kalem için bol).</summary>
    public const long MaxRequestBodyBytes = 1024 * 1024;
}
