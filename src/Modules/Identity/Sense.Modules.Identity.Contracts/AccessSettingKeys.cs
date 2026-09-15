namespace Sense.Modules.Identity.Contracts;

/// <summary>
/// Kiracı "Erişim ayarları" anahtarları (TenantSettings.Access.*; Faz 0 madde 1, 11.7 yol haritası).
/// Diğer modüller bu sabitleri <c>Sense.Shared.Contracts.Security.IAccessSettingsService.IsEnabledAsync</c> ile
/// doğrudan okuyabilir veya <c>[RequiresAccessSetting(...)]</c> ile handler'larını gater edebilir
/// (bu proje yalnızca Kernel/Shared.Contracts'a bağımlıdır — Modules_TalkOnlyThroughContracts kuralı).
/// </summary>
public static class AccessSettingKeys
{
    // Ana sayfa (Home)

    /// <summary>
    /// VARSAYIM: kaynak videoda (part8 #10) bu anahtarın adı okunamadı; en iyi tahmin "kendi birim/yönetici/ekip
    /// bilgisi" ana sayfa kartıdır (→ docs/analysis/kolayik-video/11.3.13, açık nokta #2). Doğrulanmalı.
    /// </summary>
    public const string HomeShowOrgInfo = "Home.ShowOrgInfo";

    public const string HomeShowUpcomingLeaves = "Home.ShowUpcomingLeaves";
    public const string HomeShowUpcomingBirthdays = "Home.ShowUpcomingBirthdays";
    public const string HomeShowEventCalendar = "Home.ShowEventCalendar";
    public const string HomeShowFiles = "Home.ShowFiles";
    public const string HomeShowAssets = "Home.ShowAssets";

    // Hesap (Account)
    public const string AccountShowWorkPhoneNumbers = "Account.ShowWorkPhoneNumbers";
    public const string AccountShowOrgChart = "Account.ShowOrgChart";
    public const string AccountShowCustomFields = "Account.ShowCustomFields";

    // Takvim (Calendar)
    public const string CalendarShowMenu = "Calendar.ShowMenu";
    public const string CalendarShowAllLeaves = "Calendar.ShowAllLeaves";

    /// <summary>Tüm anahtarlar (katalog listeleme, testler).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        HomeShowOrgInfo, HomeShowUpcomingLeaves, HomeShowUpcomingBirthdays, HomeShowEventCalendar, HomeShowFiles, HomeShowAssets,
        AccountShowWorkPhoneNumbers, AccountShowOrgChart, AccountShowCustomFields,
        CalendarShowMenu, CalendarShowAllLeaves,
    ];
}
