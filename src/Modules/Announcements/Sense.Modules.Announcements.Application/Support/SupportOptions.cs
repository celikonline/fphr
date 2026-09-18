namespace Sense.Modules.Announcements.Application.Support;

/// <summary>
/// Support ayarları (appsettings "Support" bölümü, 11.3.13: SupportOptions{ContactEmail, SupportEmail,
/// HelpVideoChannelUrl, LiveChat} — kodda adres/URL sabiti yok). LiveChat Faz 8 kararı, bu pakette kullanılmıyor.
/// </summary>
public sealed class SupportOptions
{
    public const string Section = "Support";

    /// <summary>Yeni destek talebi bildirimlerinin gönderileceği adres (EmailSupportChannel varsayılan adaptörü).</summary>
    public string SupportEmail { get; set; } = string.Empty;

    /// <summary>Genel iletişim adresi (Yardım Merkezi / güven sayfası gibi statik metinlerde gösterilir).</summary>
    public string ContactEmail { get; set; } = string.Empty;

    public string? HelpVideoChannelUrl { get; set; }

    public LiveChatOptions LiveChat { get; set; } = new();
}

/// <summary>[Aday] Faz 8: dış canlı sohbet sağlayıcısı (Intercom/Crisp benzeri). Varsayılan kapalı.</summary>
public sealed class LiveChatOptions
{
    public bool Enabled { get; set; }
    public string? Provider { get; set; }
    public string? AppId { get; set; }
}
