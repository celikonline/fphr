using Sense.Shared.Contracts.Security;

namespace Sense.Modules.DataTransfer.Contracts;

/// <summary>DataTransfer module permission definitions (running a template also needs the template's module permission).</summary>
public static class DataTransferPermissions
{
    private const string Module = "datatransfer";
    private const string Group = "Veri Aktarımı";

    public const string ImportViewKey = "datatransfer.import.view";
    public const string ImportRunKey = "datatransfer.import.run";
    public const string TemplateDownloadKey = "datatransfer.template.download";
    public const string ResultDownloadKey = "datatransfer.result.download";

    public static readonly Permission ImportView = new(
        ImportViewKey, Module, "İçe Aktarmaları Görüntüleme", Group, "İçe aktarma şablonlarını, geçmişini ve satır sonuçlarını görüntüleme");

    public static readonly Permission ImportRun = new(
        ImportRunKey, Module, "İçe Aktarma Çalıştırma", Group, "Dosya yükleme, sütun eşleme ve içe aktarma çalıştırma");

    public static readonly Permission TemplateDownload = new(
        TemplateDownloadKey, Module, "İçe Aktarma Şablonu İndirme", Group, "İçe aktarma şablon dosyalarını indirme");

    public static readonly Permission ResultDownload = new(
        ResultDownloadKey, Module, "İçe Aktarma Sonucu İndirme", Group, "İçe aktarma sonuç dosyalarını indirme");

    /// <summary>All DataTransfer module permissions.</summary>
    public static IEnumerable<Permission> All => [ImportView, ImportRun, TemplateDownload, ResultDownload];
}
