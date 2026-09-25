namespace Sense.Modules.DataTransfer.Application.ImportJobs;

/// <summary>
/// Yüklenen içe aktarma dosyalarının saklanması (yapılandırma <c>DataTransfer:Import</c>). Gizli sütunlu şablonların (ör. terminal kart/PIN)
/// kaynak dosyası iş bitince silinir; hiç çalıştırılmayan işin dosyası yüklemeden <see cref="SensitiveSourceFileTtlHours"/> saat sonra
/// zamanlanmış temizleme işiyle (saatlik) silinir.
/// </summary>
public sealed class ImportRetentionOptions
{
    public const string Section = "DataTransfer:Import";

    public const int DefaultSensitiveSourceFileTtlHours = 24;

    /// <summary>Gizli sütunlu şablonun yüklenen dosyasının azami ömrü (saat, en az 1).</summary>
    public int SensitiveSourceFileTtlHours { get; set; } = DefaultSensitiveSourceFileTtlHours;

    public TimeSpan SensitiveSourceFileTtl => TimeSpan.FromHours(Math.Max(1, SensitiveSourceFileTtlHours));
}
