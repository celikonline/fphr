namespace Sense.Modules.Identity.Domain.Tenants;

/// <summary>
/// Kiracı "Erişim ayarları" sayfası (Ayarlar &gt; Sistem ayarları &gt; Erişim ayarları; Faz 0 madde 1, 11.7 yol haritası).
/// Çalışan/birim yöneticisi görünürlük anahtarları: ana sayfa widget'ları, hesap sekmeleri, takvim menüsü.
/// Değişmez (record); <see cref="Tenant.UpdateAccessSettings"/> ile tamamı birlikte güncellenir.
/// Yeni anahtar eklemek (gelecek Faz'larda: tatil takvimi seçimi, veri aktarım ayarları vb.) bu kaydın alan
/// listesini genişletmekle olur — JSON kolonu olduğundan mevcut satırlar eksik alanlar için C# varsayımını kullanır,
/// ayrı bir migration gerekmez (yalnız yeni bir "bölüm" eklemek, örn. Tenant.HomeSettings, yeni kolon ister).
/// </summary>
public sealed record TenantAccessSettings
{
    // Ana sayfa (Home)

    /// <summary>
    /// Ana sayfadaki "birim/yönetici/ekip bilgisi" widget'ı.
    /// VARSAYIM: Kaynak videoda (part8 #10) Ana sayfa bölümünün ilk anahtarının adı okunamadı
    /// ("çalışan bağlamaya / birimlerle ilgili bilgiler" — bkz. 11.3.13 açık nokta #2). Bu isim ve anlam
    /// (kendi birim/yönetici/ekip bilgisi kartının ana sayfada görünürlüğü) en iyi tahmindir; doğrulanmalı.
    /// </summary>
    public bool ShowOrgInfo { get; init; } = true;

    public bool ShowUpcomingLeaves { get; init; } = true;

    public bool ShowUpcomingBirthdays { get; init; } = true;

    public bool ShowEventCalendar { get; init; } = true;

    public bool ShowFiles { get; init; } = true;

    public bool ShowAssets { get; init; } = true;

    // Hesap (Account)

    public bool ShowWorkPhoneNumbers { get; init; } = true;

    public bool ShowOrgChart { get; init; } = true;

    /// <summary>Varsayılan kapalı (11.3.13 açık nokta #6: kaynakta yalnız bu ve <see cref="ShowAllLeavesInCalendar"/> için varsayılan kesin doğrulandı).</summary>
    public bool ShowCustomFields { get; init; }

    // Takvim (Calendar)

    public bool ShowCalendarMenu { get; init; } = true;

    /// <summary>Kapalıysa yaklaşan izinler/takvim yalnız kendi birimini kapsar; açıksa şirket geneli.</summary>
    public bool ShowAllLeavesInCalendar { get; init; }

    public static readonly TenantAccessSettings Default = new();
}
