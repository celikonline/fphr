namespace Sense.Shared.Contracts.Context;

/// <summary>İstek/iş kapsamındaki kiracı. JWT claim, API key, slug veya job argümanından çözülür.</summary>
public interface ITenantContext
{
    /// <summary>Çözülmemişse <see cref="InvalidOperationException"/> fırlatır.</summary>
    Guid TenantId { get; }

    bool IsResolved { get; }

    string? TenantSlug { get; }
}

/// <summary>Kiracı bağlamını job/outbox işleyicileri için elle kurar.</summary>
public interface ITenantContextSetter
{
    IDisposable BeginScope(Guid tenantId, string? slug = null);
}

/// <summary>İstek yapan kullanıcı.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    Guid? EmployeeId { get; }

    string? Email { get; }

    string? DisplayName { get; }

    IReadOnlySet<string> Roles { get; }

    /// <summary>Platform (Sense ekibi) kullanıcısı; tenant dışı uçlara erişir.</summary>
    bool IsPlatformAdmin { get; }

    /// <summary>Impersonation varsa gerçek yönetici kullanıcı.</summary>
    Guid? ImpersonatorUserId { get; }

    /// <summary>API key ile gelen çağrılar için anahtar kimliği.</summary>
    Guid? ApiKeyId { get; }

    string? CorrelationId { get; }

    string? IpAddress { get; }
}
