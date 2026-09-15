using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application;

/// <summary>Parola hash'leme (Infrastructure: ASP.NET Core PasswordHasher, PBKDF2).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}

/// <summary>Token/kod hash'leme (SHA-256) ve rastgele token üretimi.</summary>
public interface ISecretGenerator
{
    string NewToken(int bytes = 32);

    string Hash(string token);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>JWT üretimi (RS256).</summary>
public interface ITokenService
{
    /// <summary>
    /// <paramref name="impersonatorUserId"/> verilirse token'a `act` ve `platform_admin` claim'leri eklenir (erişim yalnız aktif
    /// grant varken geçerlidir, bkz. IImpersonationSessionValidator); <paramref name="maxExpiresAtUtc"/> verilirse token süresi bununla sınırlanır
    /// (ör. onaylı impersonation grantının ExpiresAt'i) — oturum, standart JWT doğrulaması sayesinde her istekte
    /// otomatik olarak sona erer.
    /// </summary>
    AccessToken IssueAccessToken(
        User user,
        Guid tenantId,
        string tenantSlug,
        IReadOnlyCollection<string> roleCodes,
        int permissionVersion,
        Guid? impersonatorUserId = null,
        DateTime? maxExpiresAtUtc = null);

    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>Tüm modüllerin katkı verdiği birleşik izin kataloğu.</summary>
public interface IPermissionCatalog
{
    IReadOnlyList<Permission> All { get; }

    bool Exists(string permission);
}

/// <summary>İzin cache'ini geçersiz kılma (kullanıcı veya kiracı bazlı).</summary>
public interface IPermissionCacheInvalidator
{
    Task InvalidateUserAsync(Guid userId, CancellationToken ct);

    Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Kimlik e-postaları (davet, parola sıfırlama). Şablon anahtarı + veri; gönderim Notifications altyapısında.</summary>
public interface IIdentityMailer
{
    Task SendInvitationAsync(string toEmail, string toName, string tenantName, string inviteUrl, DateTime expiresAtUtc, CancellationToken ct);

    Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct);

    Task SendWelcomeAsync(string toEmail, string toName, string tenantName, string loginUrl, CancellationToken ct);
}

/// <summary>Ön yüz bağlantıları (davet kabul, parola sıfırlama).</summary>
public interface IFrontendLinks
{
    string InviteAccept(string token);

    string PasswordReset(string token);

    string Login(string tenantSlug);
}

/// <summary>Hassas alan şifreleme (TOTP secret vb.). AES-GCM, anahtar Key Vault/user-secrets.</summary>
public interface IEncryptionProvider
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}
