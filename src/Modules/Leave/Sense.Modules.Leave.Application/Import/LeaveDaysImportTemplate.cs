using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Leave.Application.Import;

/// <summary>
/// "İzin Günleri" Excel import template (11.3.15 leave.days): historical/opening leave usage for employees already
/// hired and assigned a rule set. Only <see cref="LeaveUnit.FullDay"/> types are supported in this first version -
/// Hourly/HalfDay rows need an explicit time column this template does not yet offer (row error IMPORT_UNSUPPORTED_UNIT).
/// </summary>
public sealed class LeaveDaysImportTemplate(ILeaveRuleSetRepository ruleSets) : IImportTemplate
{
    private const string KeyPrefix = "import.template.leave.days.";

    public string Code => LeaveDaysImportColumns.TemplateCode;

    public string NameKey => KeyPrefix + "name";

    public string DescriptionKey => KeyPrefix + "description";

    public string RequiredPermission => LeavePermissions.RequestsImport.Key;

    public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert];

    public IReadOnlyList<string> InstructionKeys { get; } =
    [
        KeyPrefix + "instruction.match",
        KeyPrefix + "instruction.type",
        KeyPrefix + "instruction.days",
        KeyPrefix + "instruction.full_day_only",
    ];

    public async Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default)
    {
        var typeOptions = (await ruleSets.ListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(set => set.Types)
            .Where(t => !t.IsDisabled)
            .Select(LeaveImportTypeNames.DisplayName)
            .DistinctBy(ImportValueParser.MatchKey)
            .OrderBy(n => n, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
            .ToList();

        return
        [
            Column(LeaveDaysImportColumns.NationalId, ImportColumnDataType.NationalId, ImportColumnRequirement.Always, isMatchKey: true),
            new(LeaveDaysImportColumns.LeaveTypeName, LeaveDaysImportColumns.HeaderKey(LeaveDaysImportColumns.LeaveTypeName), HeaderText: null,
                ImportColumnDataType.Lookup, ImportColumnRequirement.Always, Options: typeOptions,
                DescriptionKey: LeaveDaysImportColumns.HeaderKey(LeaveDaysImportColumns.LeaveTypeName) + ".description"),
            Column(LeaveDaysImportColumns.StartDate, ImportColumnDataType.Date, ImportColumnRequirement.Always),
            Column(LeaveDaysImportColumns.EndDate, ImportColumnDataType.Date, ImportColumnRequirement.Always),
            Column(LeaveDaysImportColumns.Days, ImportColumnDataType.Text, ImportColumnRequirement.Always),
            Column(LeaveDaysImportColumns.Description, ImportColumnDataType.Text, ImportColumnRequirement.Optional),
        ];
    }

    public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns) =>
    [
        new(true, new Dictionary<string, string>
        {
            [LeaveDaysImportColumns.NationalId] = "10000000146",
            [LeaveDaysImportColumns.LeaveTypeName] = "Yıllık izin",
            [LeaveDaysImportColumns.StartDate] = "01.09.2026",
            [LeaveDaysImportColumns.EndDate] = "05.09.2026",
            [LeaveDaysImportColumns.Days] = "5",
            [LeaveDaysImportColumns.Description] = "Kolay İK'ya geçiş nedeniyle aktarılan denkleştirme iznidir.",
        }, KeyPrefix + "sample.valid"),
        new(false, new Dictionary<string, string>
        {
            [LeaveDaysImportColumns.NationalId] = "10000000146",
            [LeaveDaysImportColumns.LeaveTypeName] = "Tanımsız İzin Türü",
            [LeaveDaysImportColumns.StartDate] = "01.09.2026",
            [LeaveDaysImportColumns.EndDate] = "01.09.2026",
            [LeaveDaysImportColumns.Days] = "1",
        }, KeyPrefix + "sample.invalid_type"),
    ];

    private static ImportColumn Column(string key, ImportColumnDataType type, ImportColumnRequirement requirement, bool isMatchKey = false) =>
        new(key, LeaveDaysImportColumns.HeaderKey(key), HeaderText: null, type, requirement, isMatchKey,
            DescriptionKey: LeaveDaysImportColumns.HeaderKey(key) + ".description");
}
