using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Login'in "MFA gerekli" yanıtındaki `mfaToken`: kullanıcı+kiracı kimliği ASP.NET Core DataProtection ile
/// imzalanıp süre damgasıyla şifrelenir; sunucuda hiçbir tablo/satır tutulmaz (PO kararı, Faz 3 Tur 3 — ek NuGet
/// paketi yok, `Microsoft.AspNetCore.DataProtection.Extensions` shared framework'ün parçası).
/// </summary>
public sealed class MfaChallengeTokenProtector : IMfaChallengeTokenProtector
{
    private const string Purpose = "identity.mfa.login";
    private const char Separator = '|';

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;

    public MfaChallengeTokenProtector(IDataProtectionProvider provider, IOptions<IdentityOptions> options)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _lifetime = TimeSpan.FromMinutes(options.Value.MfaTokenMinutes);
    }

    public string Protect(Guid userId, Guid tenantId) =>
        _protector.Protect(userId.ToString("N", CultureInfo.InvariantCulture) + Separator + tenantId.ToString("N", CultureInfo.InvariantCulture), _lifetime);

    public bool TryUnprotect(string token, out Guid userId, out Guid tenantId)
    {
        userId = Guid.Empty;
        tenantId = Guid.Empty;
        try
        {
            var payload = _protector.Unprotect(token, out _);
            var parts = payload.Split(Separator);
            return parts.Length == 2
                && Guid.TryParseExact(parts[0], "N", out userId)
                && Guid.TryParseExact(parts[1], "N", out tenantId);
        }
        catch (CryptographicException)
        {
            // Süresi dolmuş ya da bozulmuş/kurcalanmış token — ikisi de aynı şekilde ele alınır (bkz. IMfaChallengeTokenProtector).
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
