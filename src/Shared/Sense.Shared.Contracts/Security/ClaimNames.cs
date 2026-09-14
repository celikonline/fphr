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
    public const string CorrelationId = "X-Correlation-Id";
    public const string ApiKey = "X-Api-Key";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string TenantSlug = "X-Tenant";
    public const string RateLimitRemaining = "X-RateLimit-Remaining";
}

/// <summary>Sistem rolü kodları tüm modüllerin görebileceği yerde (yetki kontrolleri için).</summary>
public static class WellKnownRoles
{
    public const string System = "System";
    public const string PlatformAdmin = "PlatformAdmin";
}
