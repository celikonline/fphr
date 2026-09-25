using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Terminals;

/// <summary>Column keys and kind tokens of the "terminal-credentials" template (Z5.4).</summary>
public static class TerminalCredentialImportColumns
{
    public const string TemplateCode = "terminal-credentials";
    public const string SicilNumarasi = "sicilNumarasi";
    public const string Kind = "kind";

    /// <summary>Kart numarası / PIN / dış kimlik: gizli değer (<see cref="ImportColumn.IsSensitive"/>).</summary>
    public const string Value = "value";

    public const string CardToken = "Kart";
    public const string PinToken = "PIN";
    public const string ExternalIdToken = "Harici";

    public static IReadOnlyList<string> KindTokens { get; } = [CardToken, PinToken, ExternalIdToken];

    private static readonly Dictionary<string, TerminalCredentialKind> KindsByToken = new(StringComparer.OrdinalIgnoreCase)
    {
        [CardToken] = TerminalCredentialKind.Card,
        ["Card"] = TerminalCredentialKind.Card,
        [PinToken] = TerminalCredentialKind.Pin,
        [ExternalIdToken] = TerminalCredentialKind.ExternalId,
        ["HARİCİ"] = TerminalCredentialKind.ExternalId,
        ["External"] = TerminalCredentialKind.ExternalId,
        ["ExternalId"] = TerminalCredentialKind.ExternalId,
    };

    public static TerminalCredentialKind? KindOf(string token) => KindsByToken.TryGetValue(token.Trim(), out var kind) ? kind : null;

    public static string HeaderKey(string key) => "import.column.terminalcredentials." + key;
}

/// <summary>
/// Toplu terminal kimliği Excel şablonu (Z5.4): Sicil no, Tür (Kart | PIN | Harici), Değer. Değer sütunu gizlidir: DataTransfer örnek değer
/// saklamaz, yüklenen dosyayı iş koşunca siler ve sonuç dosyasına yalnız satır sonucunu yazar. Kimlik özeti pepper'ı yoksa şablon hazır değildir
/// (<see cref="CheckReady"/> → 503): dosya hiç kabul edilmez, hiçbir satır işlenmez.
/// </summary>
public sealed class TerminalCredentialImportTemplate(ITerminalCredentialHasher hasher) : IImportTemplate, IImportTemplateReadiness
{
    private const string KeyPrefix = "import.template.terminalcredentials.";

    public string Code => TerminalCredentialImportColumns.TemplateCode;

    public string NameKey => KeyPrefix + "name";

    public string DescriptionKey => KeyPrefix + "description";

    public string RequiredPermission => AttendancePermissions.TerminalManage.Key;

    public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert];

    public IReadOnlyList<string> InstructionKeys { get; } =
    [
        KeyPrefix + "instruction.match",
        KeyPrefix + "instruction.format",
        KeyPrefix + "instruction.in_use",
        KeyPrefix + "instruction.security",
    ];

    public Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImportColumn>>(
        [
            Column(TerminalCredentialImportColumns.SicilNumarasi, ImportColumnDataType.Text, isMatchKey: true),
            Column(TerminalCredentialImportColumns.Kind, ImportColumnDataType.Lookup, options: TerminalCredentialImportColumns.KindTokens),
            Column(TerminalCredentialImportColumns.Value, ImportColumnDataType.Text, isSensitive: true),
        ]);

    public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns) =>
    [
        Sample(true, "S-1001", TerminalCredentialImportColumns.CardToken, "04A1B2C3D4", "sample.card"),
        Sample(true, "S-1002", TerminalCredentialImportColumns.PinToken, "048215", "sample.pin"),
        Sample(false, "S-1003", TerminalCredentialImportColumns.PinToken, "12A", "sample.invalid_pin"),
    ];

    public Result CheckReady() => hasher.IsAvailable ? Result.Success() : Error.ServiceUnavailable(TerminalErrors.CredentialPepperMissing);

    private static ImportSampleRow Sample(bool isValid, string sicil, string kind, string value, string note) =>
        new(isValid, new Dictionary<string, string>
        {
            [TerminalCredentialImportColumns.SicilNumarasi] = sicil,
            [TerminalCredentialImportColumns.Kind] = kind,
            [TerminalCredentialImportColumns.Value] = value,
        }, KeyPrefix + note);

    private static ImportColumn Column(
        string key, ImportColumnDataType type, bool isMatchKey = false, IReadOnlyList<string>? options = null, bool isSensitive = false) =>
        new(key, TerminalCredentialImportColumns.HeaderKey(key), HeaderText: null, type, ImportColumnRequirement.Always, isMatchKey, options,
            TerminalCredentialImportColumns.HeaderKey(key) + ".description", isSensitive);
}
