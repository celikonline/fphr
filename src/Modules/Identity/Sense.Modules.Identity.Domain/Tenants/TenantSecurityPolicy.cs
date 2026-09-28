namespace Sense.Modules.Identity.Domain.Tenants;

/// <summary>
/// Kiracı "Güvenlik politikası" (Ayarlar &gt; Sistem ayarları &gt; Güvenlik politikası; senseik-051). Belirli sistem
/// rollerinde 2FA'yı zorunlu kılar: <see cref="RequireMfaForRoles"/> içindeki rol kodlarından birine sahip bir
/// kullanıcı MFA'sı kapalıyken oturum açtığında, ön yüz onu kritik ekranlara erişmeden önce kurulum akışına
/// yönlendirir (bkz. <c>GetMeHandler</c> → <c>FeaturesDto.MfaSetupRequired</c>).
/// Değişmez (record); <see cref="Tenant.UpdateSecurityPolicy"/> ile tamamı birlikte güncellenir. Erişim ayarlarıyla
/// aynı tek-jsonb-kolon kalıbı (bkz. <see cref="TenantAccessSettings"/>): yeni bir alan eklemek ayrı migration
/// istemez, mevcut satırlar eksik alanlar için C# varsayımını kullanır.
/// </summary>
public sealed record TenantSecurityPolicy
{
    /// <summary>Bu rol kodlarından (Owner, Admin, HR, PayrollSpecialist…) birine sahip kullanıcılar için 2FA zorunlu.
    /// Boşsa hiçbir rol için zorunluluk yoktur (varsayılan).</summary>
    public IReadOnlyList<string> RequireMfaForRoles
    {
        // EF Core materializes a jsonb value that lacks the property (the migration default '{}' on tenants that
        // existed before senseik-051) with a null list; the getter keeps every reader (GetMe, DTO mapping) null-safe.
        get => enforcedRoles ?? [];
        init => enforcedRoles = value;
    }

    private readonly IReadOnlyList<string>? enforcedRoles;

    public static readonly TenantSecurityPolicy Default = new();

    /// <summary>
    /// Kullanıcının rollerinden biri zorunlu listede VE MFA'sı kapalıysa true — ön yüzün kurulum akışına
    /// yönlendirmesi gereken durum. MFA zaten açıksa veya zorunlu rol listesi boşsa her zaman false.
    /// </summary>
    public bool RequiresMfaSetup(IEnumerable<string> userRoleCodes, bool mfaEnabled) =>
        !mfaEnabled && RequiresMfa(userRoleCodes);

    /// <summary>Kullanıcının rollerinden biri zorunlu listede mi (MFA durumundan bağımsız). FG-022: true ise kullanıcı
    /// MFA'yı self-servis kapatamaz (<c>DisableMfaHandler</c>), güvenlik ekranı bunu <c>MfaStatusDto.RequiredByPolicy</c> ile gösterir.</summary>
    public bool RequiresMfa(IEnumerable<string> userRoleCodes) =>
        RequireMfaForRoles.Count > 0
        && userRoleCodes.Any(code => RequireMfaForRoles.Contains(code, StringComparer.OrdinalIgnoreCase));
}
