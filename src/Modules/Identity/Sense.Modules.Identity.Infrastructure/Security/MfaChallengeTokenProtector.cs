using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Login'in "MFA gerekli" yanıtındaki `mfaToken`: kullanıcı+kiracı kimliği ASP.NET Core DataProtection ile
/// imzalanıp şifrelenir; sunucuda hiçbir tablo/satır tutulmaz (PO kararı, Faz 3 Tur 3 — ek NuGet
/// paketi yok, `Microsoft.AspNetCore.DataProtection.Extensions` shared framework'ün parçası).
/// Süre aşımı kasıtlı olarak `ITimeLimitedDataProtector`'a değil, enjekte <see cref="TimeProvider"/> ile
/// karşılaştırılan gömülü `issuedAt` damgasına bakılarak denetlenir: DataProtection'ın iç saati sahte
/// saate (test) uymaz, oysa bu katmandaki her şey enjekte saate göre çalışır.
/// </summary>
public sealed class MfaChallengeTokenProtector : IMfaChallengeTokenProtector
{
    private const string Purpose = "identity.mfa.login";
    private const char Separator = '|';

    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;

    public MfaChallengeTokenProtector(IDataProtectionProvider provider, IOptions<IdentityOptions> options, TimeProvider clock)
    {
        _protector = provider.CreateProtector(Purpose);
        _clock = clock;
        _lifetime = TimeSpan.FromMinutes(options.Value.MfaTokenMinutes);
    }

    public string Protect(Guid userId, Guid tenantId) =>
        _protector.Protect(
            userId.ToString("N", CultureInfo.InvariantCulture) + Separator +
            tenantId.ToString("N", CultureInfo.InvariantCulture) + Separator +
            _clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    public bool TryUnprotect(string token, out Guid userId, out Guid tenantId)
    {
        userId = Guid.Empty;
        tenantId = Guid.Empty;
        try
        {
            var parts = _protector.Unprotect(token).Split(Separator);
            if (parts.Length != 3
                || !Guid.TryParseExact(parts[0], "N", out userId)
                || !Guid.TryParseExact(parts[1], "N", out tenantId)
                || !long.TryParse(parts[2], CultureInfo.InvariantCulture, out var issuedAtSeconds))
            {
                return false;
            }

            // Eski format (damgasız iki parçalı) buraya düşer ve süresi dolmuş sayılır: 5 dakikalık login
            // token'ları dağıtımda geçersiz kalır, kullanıcı yeniden giriş yapar.
            return _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds) <= _lifetime;
        }
        catch (CryptographicException)
        {
            // Bozulmuş/kurcalanmış token — süre aşımıyla aynı şekilde ele alınır (bkz. IMfaChallengeTokenProtector).
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
