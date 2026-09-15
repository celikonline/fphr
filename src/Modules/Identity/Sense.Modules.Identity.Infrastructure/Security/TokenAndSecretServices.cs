using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Security;
using IdentityOptions = Sense.Modules.Identity.Application.IdentityOptions;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>JWT ayarları (host'un Auth bölümüyle aynı şema; Identity yalnız üretim tarafını okur).</summary>
public sealed class JwtIssuerOptions
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;
}

public sealed class JwtTokenService(RsaSecurityKey key, IOptions<JwtIssuerOptions> jwt, IOptions<IdentityOptions> identity, TimeProvider clock) : ITokenService
{
    private static readonly JwtSecurityTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(identity.Value.RefreshTokenDays);

    public AccessToken IssueAccessToken(
        User user,
        Guid tenantId,
        string tenantSlug,
        IReadOnlyCollection<string> roleCodes,
        int permissionVersion,
        Guid? impersonatorUserId = null,
        DateTime? maxExpiresAtUtc = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(identity.Value.AccessTokenMinutes);
        if (maxExpiresAtUtc is { } cap && cap < expires)
        {
            expires = cap;
        }

        var claims = new List<Claim>
        {
            new(ClaimNames.Subject, user.Id.ToString()),
            new(ClaimNames.Tenant, tenantId.ToString()),
            new(ClaimNames.TenantSlug, tenantSlug),
            new(ClaimNames.Email, user.Email),
            new(ClaimNames.Name, user.FullName),
            new(ClaimNames.Language, user.PreferredLanguage),
            new(ClaimNames.PermissionVersion, permissionVersion.ToString(CultureInfo.InvariantCulture)),
            new(ClaimNames.AuthMethods, user.MfaEnabled ? AuthMethodValues.Mfa : AuthMethodValues.Password),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        if (user.EmployeeId is { } eid)
        {
            claims.Add(new Claim(ClaimNames.Employee, eid.ToString()));
        }

        claims.AddRange(roleCodes.Select(r => new Claim(ClaimNames.Roles, r)));

        if (impersonatorUserId is { } impersonator)
        {
            // Impersonation oturumu: gerçek aktör `act` claim'inde izlenir. Bu claim'ler tek başına yetki vermez; her istekte
            // ImpersonationSessionValidator hedef kiracıda Active, süresi dolmamış ve iptal edilmemiş bir grant arar.
            claims.Add(new Claim(ClaimNames.Impersonator, impersonator.ToString()));
            claims.Add(new Claim(ClaimNames.PlatformAdmin, ClaimNames.TrueValue));
        }

        var token = new JwtSecurityToken(
            issuer: jwt.Value.Issuer,
            audience: jwt.Value.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));

        return new AccessToken(Handler.WriteToken(token), expires);
    }
}

/// <summary>PBKDF2 (ASP.NET Core PasswordHasher, framework içi). User tipi yalnız generic parametre.</summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        return _hasher.VerifyHashedPassword(null!, hash, password) is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}

/// <summary>Kriptografik rastgele token (base64url) ve SHA-256 hash (hex).</summary>
public sealed class SecretGenerator : ISecretGenerator
{
    public string NewToken(int bytes = IdentityDefaults.TokenBytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Base64UrlEncoder.Encode(buffer);
    }

    public string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>AES-GCM alan şifreleme. Anahtar: Security:EncryptionKey (base64, 32 byte). Geliştirmede yoksa deterministik türetilir.</summary>
public sealed class AesGcmEncryptionProvider(IOptions<SecurityOptions> options) : IEncryptionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key = ResolveKey(options.Value);

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Decrypt(string ciphertext)
    {
        var all = Convert.FromBase64String(ciphertext);
        var nonce = all.AsSpan(0, NonceSize);
        var tag = all.AsSpan(NonceSize, TagSize);
        var cipher = all.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] ResolveKey(SecurityOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.EncryptionKey))
        {
            return Convert.FromBase64String(o.EncryptionKey);
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(o.DevelopmentKeySeed));
    }
}

public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>Base64 kodlu 32 byte AES anahtarı (prod: Key Vault).</summary>
    public string? EncryptionKey { get; set; }

    public string DevelopmentKeySeed { get; set; } = SecurityDefaults.DevelopmentKeySeed;
}

public static class SecurityDefaults
{
    public const string DevelopmentKeySeed = "senseik-development-only-key";
}
