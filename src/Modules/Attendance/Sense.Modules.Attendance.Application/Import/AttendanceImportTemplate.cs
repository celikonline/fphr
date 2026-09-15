using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Attendance.Application.Import;

/// <summary>Column keys of the "attendance.pdks" template (written to the template's hidden key row).</summary>
public static class AttendanceImportColumns
{
    public const string TemplateCode = "attendance.pdks";
    public const string NationalId = "employeeNationalId";
    public const string Date = "date";
    public const string CheckIn = "checkIn";
    public const string CheckOut = "checkOut";

    public static string HeaderKey(string key) => "import.column.attendance." + key;
}

/// <summary>Row error codes specific to the "attendance.pdks" template (11.3.6 open question #1: match key/columns are ours to decide).</summary>
public static class AttendanceImportErrorCodes
{
    public const string InvalidTime = "attendance.import.error.invalid_time";
    public const string CheckOutBeforeCheckIn = "attendance.import.error.check_out_before_check_in";
}

/// <summary>
/// PDKS Excel import template (11.3.6, package A2b): "Çalışan (Kimlik No)", Tarih, Giriş, Çıkış. Matches the employee
/// by national id (the spec leaves the match key undecided - "videoda okunmuyor, şablon indirilerek doğrulanmalı" -
/// full-name matching is deliberately left out of this first pass as ambiguity-prone). Re-importing the same
/// employee+day overwrites the previous record, same as <see cref="AttendanceDays.RecordAttendanceCommand"/>.
/// </summary>
public sealed class AttendanceImportTemplate : IImportTemplate
{
    private const string KeyPrefix = "import.template.attendance.pdks.";

    public string Code => AttendanceImportColumns.TemplateCode;

    public string NameKey => KeyPrefix + "name";

    public string DescriptionKey => KeyPrefix + "description";

    public string RequiredPermission => AttendancePermissions.TimesheetImportKey;

    public IReadOnlyList<ImportMode> SupportedModes { get; } = [ImportMode.Upsert];

    public IReadOnlyList<string> InstructionKeys { get; } =
    [
        KeyPrefix + "instruction.match",
        KeyPrefix + "instruction.overwrite",
        KeyPrefix + "instruction.time_format",
    ];

    public Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImportColumn>>(
        [
            Column(AttendanceImportColumns.NationalId, ImportColumnDataType.NationalId, ImportColumnRequirement.Always, isMatchKey: true),
            Column(AttendanceImportColumns.Date, ImportColumnDataType.Date, ImportColumnRequirement.Always),
            Column(AttendanceImportColumns.CheckIn, ImportColumnDataType.Text, ImportColumnRequirement.Optional),
            Column(AttendanceImportColumns.CheckOut, ImportColumnDataType.Text, ImportColumnRequirement.Optional),
        ]);

    public IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns) =>
    [
        new(true, new Dictionary<string, string>
        {
            [AttendanceImportColumns.NationalId] = "10000000146",
            [AttendanceImportColumns.Date] = "01.09.2026",
            [AttendanceImportColumns.CheckIn] = "09:00",
            [AttendanceImportColumns.CheckOut] = "18:05",
        }, KeyPrefix + "sample.valid"),
        new(false, new Dictionary<string, string>
        {
            [AttendanceImportColumns.NationalId] = "10000000146",
            [AttendanceImportColumns.Date] = "01.09.2026",
            [AttendanceImportColumns.CheckIn] = "18:00",
            [AttendanceImportColumns.CheckOut] = "09:00",
        }, KeyPrefix + "sample.invalid_order"),
    ];

    private static ImportColumn Column(string key, ImportColumnDataType type, ImportColumnRequirement requirement, bool isMatchKey = false) =>
        new(key, AttendanceImportColumns.HeaderKey(key), HeaderText: null, type, requirement, isMatchKey, DescriptionKey: AttendanceImportColumns.HeaderKey(key) + ".description");
}
