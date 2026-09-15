using Sense.Shared.Contracts.Security;

namespace Sense.Modules.DataTransfer.Contracts;

/// <summary>DataTransfer module permission definitions (running a template also needs the template's module permission).</summary>
public static class DataTransferPermissions
{
    private const string Module = "datatransfer";
    private const string Group = "DataTransfer";

    public const string ImportViewKey = "datatransfer.import.view";
    public const string ImportRunKey = "datatransfer.import.run";
    public const string TemplateDownloadKey = "datatransfer.template.download";
    public const string ResultDownloadKey = "datatransfer.result.download";

    public static readonly Permission ImportView = new(
        ImportViewKey, Module, "View Imports", Group, "View import templates, import history and row outcomes");

    public static readonly Permission ImportRun = new(
        ImportRunKey, Module, "Run Imports", Group, "Upload files, map columns and run imports");

    public static readonly Permission TemplateDownload = new(
        TemplateDownloadKey, Module, "Download Import Templates", Group, "Download import template files");

    public static readonly Permission ResultDownload = new(
        ResultDownloadKey, Module, "Download Import Results", Group, "Download import result files");

    /// <summary>All DataTransfer module permissions.</summary>
    public static IEnumerable<Permission> All => [ImportView, ImportRun, TemplateDownload, ResultDownload];
}
