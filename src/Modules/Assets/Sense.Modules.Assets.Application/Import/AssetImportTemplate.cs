using Sense.Modules.Assets.Contracts;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Assets.Application.Import;

/// <summary>Column keys of the "assets.zimmet" template (written to the template's hidden key row).</summary>
public static class AssetImportColumns
{
    public const string TemplateCode = "assets.zimmet";
    public const string NationalId = "employeeNationalId";
    public const string CategoryName = "categoryName";
    public const string SerialNumber = "serialNumber";
    public const string AssignedAt = "assignedAt";
    public const string ReturnedAt = "returnedAt";
    public const string Note = "note";
    public const string Name = "name";

    public static string HeaderKey(string key) => "import.column.assets." + key;
}

/// <summary>
/// Zimmet aktarım şablonu (11.3.08 md.5/md.67): Kimlik no / Kategori / Seri no / Veriliş tarihi / İade tarihi /
/// Açıklama / Varlık adı. Çalışan ve tarihler boşsa satır boşta envanterdir. Kategori adı bulunamazsa find-or-create (kod otomatik üretilir); seri no doluysa aynı kategori + seri
/// no'lu mevcut varlık yeniden kullanılır, boşsa yeni varlık oluşturulur. İade tarihi doluysa satır kapalı bir
/// atama (Unassigned + ReturnedAt) üretir, boşsa açık atama (Assigned). Plandaki "E-posta" kolonu kasıtlı olarak
/// yok: zimmet bildirimi artık her atama/iade işleminde otomatik gidiyor (roadmap #116), satır bazlı bir
/// "bilgilendir" seçimine ihtiyaç kalmadı.
/// TK-003: "Sadece güncelleme" (UpdateOnly) modu da desteklenir - bkz. AssetImportRowHandler.
/// </summary>
public sealed class AssetImportTemplate : IImportTemplate
{
    private const string KeyPrefix = "import.template.assets.zimmet.";

    public string Code => AssetImportColumns.TemplateCode;

    public string NameKey => KeyPrefix + "name";

    public string DescriptionKey => KeyPrefix + "description";

    public string RequiredPermission => AssetsPermissions.AssetsImport.Key;

    public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert, ImportMode.UpdateOnly];

    public IReadOnlyList<string> InstructionKeys { get; } =
    [
        KeyPrefix + "instruction.match",
        KeyPrefix + "instruction.category",
        KeyPrefix + "instruction.returned",
        KeyPrefix + "instruction.inventory",
    ];

    public Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImportColumn>>(
        [
            Column(AssetImportColumns.NationalId, ImportColumnDataType.NationalId, ImportColumnRequirement.Optional),
            Column(AssetImportColumns.CategoryName, ImportColumnDataType.Text, ImportColumnRequirement.Always),
            Column(AssetImportColumns.SerialNumber, ImportColumnDataType.Text, ImportColumnRequirement.Optional),
            Column(AssetImportColumns.AssignedAt, ImportColumnDataType.Date, ImportColumnRequirement.Optional),
            Column(AssetImportColumns.ReturnedAt, ImportColumnDataType.Date, ImportColumnRequirement.Optional),
            Column(AssetImportColumns.Note, ImportColumnDataType.Text, ImportColumnRequirement.Optional),
            Column(AssetImportColumns.Name, ImportColumnDataType.Text, ImportColumnRequirement.Optional),
        ]);

    public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns) =>
    [
        new(true, new Dictionary<string, string>
        {
            [AssetImportColumns.NationalId] = "10000000146",
            [AssetImportColumns.CategoryName] = "Bilgisayar",
            [AssetImportColumns.SerialNumber] = "SN-00123",
            [AssetImportColumns.AssignedAt] = "01.09.2026",
            [AssetImportColumns.ReturnedAt] = string.Empty,
            [AssetImportColumns.Note] = "MacBook Pro 14",
        }, KeyPrefix + "sample.open"),
        new(true, new Dictionary<string, string>
        {
            [AssetImportColumns.NationalId] = "10000000153",
            [AssetImportColumns.CategoryName] = "Telefon",
            [AssetImportColumns.SerialNumber] = string.Empty,
            [AssetImportColumns.AssignedAt] = "01.03.2026",
            [AssetImportColumns.ReturnedAt] = "15.08.2026",
            [AssetImportColumns.Note] = string.Empty,
        }, KeyPrefix + "sample.closed"),
        new(true, new Dictionary<string, string>
        {
            [AssetImportColumns.NationalId] = string.Empty,
            [AssetImportColumns.CategoryName] = "Bilgisayar",
            [AssetImportColumns.SerialNumber] = "SN-00124",
            [AssetImportColumns.AssignedAt] = string.Empty,
            [AssetImportColumns.ReturnedAt] = string.Empty,
            [AssetImportColumns.Note] = string.Empty,
            [AssetImportColumns.Name] = "Elite book",
        }, KeyPrefix + "sample.inventory"),
    ];

    private static ImportColumn Column(string key, ImportColumnDataType type, ImportColumnRequirement requirement, bool isMatchKey = false) =>
        new(key, AssetImportColumns.HeaderKey(key), HeaderText: null, type, requirement, isMatchKey, DescriptionKey: AssetImportColumns.HeaderKey(key) + ".description");
}
