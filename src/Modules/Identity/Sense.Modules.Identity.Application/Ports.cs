using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Persistence;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application;

/// <summary>Identity'nin SaveChanges portu; komut handler'ları için <see cref="IUnitOfWork"/> zaten pipeline'ın
/// UnitOfWorkBehaviour'ı ile dolaylı çözülür - bu, doğrudan enjekte edilen tüketiciler (ör. integration event
/// handler'ları, Performance/Assets modüllerindeki <c>I*UnitOfWork</c> kalıbıyla aynı) içindir.</summary>
public interface IIdentityUnitOfWork : IUnitOfWork;

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

/// <summary>Kiracının modül aç/kapa önbelleğini (IModuleAccessService) geçersiz kılar (senseik-057, Modüller sayfası "Kaydet").</summary>
public interface IModuleSettingsCacheInvalidator
{
    Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Kimlik e-postaları (davet, parola sıfırlama). Şablon anahtarı + veri; gönderim Notifications altyapısında.</summary>
public interface IIdentityMailer
{
    Task SendInvitationAsync(string toEmail, string toName, string tenantName, string inviteUrl, DateTime expiresAtUtc, CancellationToken ct);

    Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct);

    Task SendWelcomeAsync(string toEmail, string toName, string tenantName, string loginUrl, CancellationToken ct);

    /// <summary>Yönetici MFA'yı sıfırladığında hedef kullanıcıya güvenlik bildirimi (G-3-19). Yalnız MFA gerçekten
    /// açıkken gönderilir - <see cref="Users.ResetUserMfaHandler"/>.</summary>
    Task SendMfaResetNoticeAsync(string toEmail, string toName, string tenantName, CancellationToken ct);
}

/// <summary>Ön yüz bağlantıları (davet kabul, parola sıfırlama).</summary>
public interface IFrontendLinks
{
    string InviteAccept(string token);

    string PasswordReset(string token);

    string Login(string tenantSlug);
}

/// <summary>TOTP (RFC 6238) secret üretimi, otpauth:// URI ve kod doğrulama. Infrastructure: BCL `HMACSHA1`, ek paket yok.</summary>
public interface ITotpService
{
    /// <summary>20 byte'lık rastgele secret, Base32 kodlu (kullanıcıya/QR'a gösterilir, şifreli olarak saklanır).</summary>
    string GenerateSecret();

    /// <summary>Authenticator uygulamaları için `otpauth://totp/...` URI'si (QR'sız da kopyalanabilir).</summary>
    string BuildOtpAuthUri(string secretBase32, string accountLabel, string issuer);

    /// <summary>30 sn adım, ±1 pencere (saat kaymasına tolerans).</summary>
    bool ValidateCode(string secretBase32, string code);
}

/// <summary>Login sonrası "MFA gerekli" durumunda verilen kısa ömürlü, durumsuz `mfaToken`. Sunucuda tablo tutmaz:
/// kullanıcı+kiracı kimliği imzalı/şifreli olarak taşınır (Infrastructure: ASP.NET Core DataProtection, 5 dk).</summary>
public interface IMfaChallengeTokenProtector
{
    string Protect(Guid userId, Guid tenantId);

    /// <summary>Süresi dolmuş veya bozulmuş token için false döner (ayrım yapılmaz — ikisi de kullanıcı için "yeniden giriş yap" demektir).</summary>
    bool TryUnprotect(string token, out Guid userId, out Guid tenantId);
}
