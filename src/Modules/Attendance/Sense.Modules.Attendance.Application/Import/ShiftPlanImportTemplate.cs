using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Attendance.Application.Import;

/// <summary>Column keys and tokens of the "shift-plan" template.</summary>
public static class ShiftPlanImportColumns
{
    public const string TemplateCode = "shift-plan";
    public const string NationalId = "employeeNationalId";
    public const string Date = "date";

    /// <summary>Vardiya tanımı kodu ya da gün türü belirteci (<see cref="WeeklyRestToken"/> vb.); açılır listede tanım kodları + belirteçler yer alır.</summary>
    public const string ShiftCode = "shiftCode";

    public const string WeeklyRestToken = "WEEKLY_REST";
    public const string RestDayToken = "REST_DAY";
    public const string PublicHolidayToken = "PUBLIC_HOLIDAY";

    public static IReadOnlyList<string> DayTypeTokens { get; } = [WeeklyRestToken, RestDayToken, PublicHolidayToken];

    public static ShiftDayType? DayTypeOf(string token) => token.ToUpperInvariant() switch
    {
        WeeklyRestToken => ShiftDayType.WeeklyRest,
        RestDayToken => ShiftDayType.RestDay,
        PublicHolidayToken => ShiftDayType.PublicHoliday,
        _ => null,
    };

    public static string HeaderKey(string key) => "import.column.shiftplan." + key;
}

/// <summary>
/// Vardiya planı Excel şablonu (Z3.2): Kimlik No, Tarih, Vardiya kodu. Kod sütunu açılır listedir (kiracının aktif vardiya tanımı kodları
/// ve gün türü belirteçleri); çalışan kimlik numarasıyla eşleştirilir, aynı çalışan+gün için yeniden yükleme üzerine yazar.
/// </summary>
public sealed class ShiftPlanImportTemplate(IShiftDefinitionRepository definitions) : IImportTemplate
{
    private const string KeyPrefix = "import.template.shiftplan.";

    public string Code => ShiftPlanImportColumns.TemplateCode;

    public string NameKey => KeyPrefix + "name";

    public string DescriptionKey => KeyPrefix + "description";

    public string RequiredPermission => AttendancePermissions.ShiftPlan.Key;

    public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert];

    public IReadOnlyList<string> InstructionKeys { get; } =
    [
        KeyPrefix + "instruction.match",
        KeyPrefix + "instruction.code",
        KeyPrefix + "instruction.locked",
    ];

    public async Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default)
    {
        var codes = (await definitions.ListAsync(includeInactive: false, cancellationToken)).Select(d => d.Code).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return
        [
            Column(ShiftPlanImportColumns.NationalId, ImportColumnDataType.NationalId, isMatchKey: true),
            Column(ShiftPlanImportColumns.Date, ImportColumnDataType.Date),
            Column(ShiftPlanImportColumns.ShiftCode, ImportColumnDataType.Lookup, options: [.. codes, .. ShiftPlanImportColumns.DayTypeTokens]),
        ];
    }

    public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns)
    {
        var firstCode = columns.FirstOrDefault(c => c.Key == ShiftPlanImportColumns.ShiftCode)?.Options?.FirstOrDefault() ?? ShiftPlanImportColumns.WeeklyRestToken;
        return
        [
            new(true, new Dictionary<string, string>
            {
                [ShiftPlanImportColumns.NationalId] = "10000000146",
                [ShiftPlanImportColumns.Date] = "01.10.2026",
                [ShiftPlanImportColumns.ShiftCode] = firstCode,
            }, KeyPrefix + "sample.valid"),
            new(true, new Dictionary<string, string>
            {
                [ShiftPlanImportColumns.NationalId] = "10000000146",
                [ShiftPlanImportColumns.Date] = "02.10.2026",
                [ShiftPlanImportColumns.ShiftCode] = ShiftPlanImportColumns.WeeklyRestToken,
            }, KeyPrefix + "sample.rest"),
            new(false, new Dictionary<string, string>
            {
                [ShiftPlanImportColumns.NationalId] = "10000000146",
                [ShiftPlanImportColumns.Date] = "03.10.2026",
                [ShiftPlanImportColumns.ShiftCode] = "YOK",
            }, KeyPrefix + "sample.invalid_code"),
        ];
    }

    private static ImportColumn Column(string key, ImportColumnDataType type, bool isMatchKey = false, IReadOnlyList<string>? options = null) =>
        new(key, ShiftPlanImportColumns.HeaderKey(key), HeaderText: null, type, ImportColumnRequirement.Always, isMatchKey, options, ShiftPlanImportColumns.HeaderKey(key) + ".description");
}
