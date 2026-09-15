namespace Sense.Shared.Contracts.Security;

/// <summary>JWT claim adları (tek kaynak: token üretimi ve middleware aynı sabitleri kullanır).</summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Tenant = "tid";
    public const string TenantSlug = "tslug";
    public const string Employee = "eid";
    public const string Email = "email";
    public const string Name = "name";
    public const string Roles = "roles";
    public const string PermissionVersion = "pv";
    public const string PlatformAdmin = "platform_admin";
    public const string Impersonator = "act";
    public const string ApiKey = "api_key";
    public const string SessionId = "sid";
    public const string AuthMethods = "amr";
    public const string Language = "lang";
    public const string TrueValue = "true";
}

public static class AuthMethodValues
{
    public const string Password = "pwd";
    public const string Mfa = "mfa";
    public const string ApiKey = "apikey";
}

/// <summary>HTTP başlık adları.</summary>
public static class SenseHeaderNames
{
    /// <summary>Uçtan uca mantıksal akış kimliği (istek + yanıt; giden HttpClient çağrılarına da taşınır). Yoksa sunucu üretir.</summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>Tek bir HTTP isteğinin kimliği (proxy/ingress'ten gelirse korunur, yoksa sunucu üretir; yanıtta döner).</summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>Yanıtta döner: W3C trace id — Jaeger/Grafana'da izi doğrudan bulmak için.</summary>
    public const string TraceId = "X-Trace-Id";

    /// <summary>İstemci uygulaması (ör. web, mobile, integration) — log/iz zenginleştirme için.</summary>
    public const string ClientApp = "X-Client-App";

    /// <summary>İstemci sürümü (ör. 1.4.2) — hangi sürümün hata ürettiğini ayırt etmek için.</summary>
    public const string ClientVersion = "X-Client-Version";

    /// <summary>İstemci platformu (ör. web, ios, android).</summary>
    public const string ClientPlatform = "X-Client-Platform";

    public const string ApiKey = "X-Api-Key";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string TenantSlug = "X-Tenant";
    public const string RateLimitRemaining = "X-RateLimit-Remaining";
    public const string RetryAfter = "Retry-After";

    /// <summary>
    /// Tarayıcıdaki JavaScript'in cross-origin yanıtta okuyabilmesi gereken başlıklar (CORS <c>Access-Control-Expose-Headers</c>).
    /// </summary>
    public static readonly string[] ExposedToClients =
    [
        CorrelationId,
        RequestId,
        TraceId,
        RateLimitRemaining,
        RetryAfter,
        "Content-Disposition",
        "ETag",
        "api-supported-versions",
    ];
}

/// <summary>Sistem rolü kodları tüm modüllerin görebileceği yerde (yetki kontrolleri için).</summary>
public static class WellKnownRoles
{
    public const string System = "System";
    public const string PlatformAdmin = "PlatformAdmin";
}
