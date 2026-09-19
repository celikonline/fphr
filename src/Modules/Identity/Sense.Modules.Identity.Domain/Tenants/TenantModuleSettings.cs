namespace Sense.Modules.Identity.Domain.Tenants;

/// <summary>
/// Kiracı "Modüller" ayarı (Ayarlar &gt; Sistem ayarları &gt; Modüller; senseik-057). Kiracının KAPATTIĞI ürün modülü
/// anahtarlarını tutar — yeni eklenen modüller bu listede olmadığından varsayılan olarak açık gelir. Anahtar kataloğu
/// ve "temel modül" kuralı <c>Sense.Shared.Contracts.Modules.ProductModules</c>'tedir; doğrulama uygulama katmanında yapılır.
/// Değişmez (record); <see cref="Tenant.UpdateModuleSettings"/> ile tamamı birlikte güncellenir. Erişim ayarları ve
/// güvenlik politikasıyla aynı tek-jsonb-kolon kalıbı (bkz. <see cref="TenantAccessSettings"/>).
/// </summary>
public sealed record TenantModuleSettings
{
    /// <summary>Kapalı modül anahtarları; tekrarsız ve sıralı (<see cref="WithDisabled"/> normalize eder). Boş = hepsi açık.</summary>
    public IReadOnlyList<string> DisabledModules { get; init; } = [];

    public static readonly TenantModuleSettings Default = new();

    /// <summary>Boş/boşluk anahtarları atar, tekrarları birleştirir, sıralar (kararlı jsonb içeriği ve eşitlik için).</summary>
    public static TenantModuleSettings WithDisabled(IEnumerable<string> moduleKeys) => new()
    {
        DisabledModules = moduleKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList(),
    };

    public bool IsDisabled(string moduleKey) => DisabledModules.Contains(moduleKey, StringComparer.Ordinal);
}
