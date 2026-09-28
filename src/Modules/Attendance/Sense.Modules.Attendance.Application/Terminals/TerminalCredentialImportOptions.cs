namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Terminal kimliği içe aktarma sınırları (yapılandırma <c>Attendance:Terminals</c>).</summary>
public sealed class TerminalCredentialImportOptions
{
    public const string Section = "Attendance:Terminals";

    public const int DefaultMaxPinRejectsPerImport = 5;

    /// <summary>
    /// Bir içe aktarmada "başka çalışanda aktif" nedeniyle reddedilebilecek en çok PIN satırı (toplu tarama hafifletmesi). Aşılınca iş bütünüyle
    /// genel bir hatayla düşer: hiçbir kimlik kaydedilmez, satır ayrıntısı verilmez, kalan PIN satırları denenmez. En az 1.
    /// </summary>
    public int MaxPinRejectsPerImport { get; set; } = DefaultMaxPinRejectsPerImport;
}
