using System.Globalization;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.DataTransfer.Application.ImportJobs;

/// <summary>Language of template/result files: "?lang=" (tr/en), falling back to the request culture, then Turkish.</summary>
public static class ImportCultures
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public static IReadOnlyList<CultureInfo> Supported { get; } = [Turkish, English];

    public static CultureInfo Resolve(string? lang)
    {
        var code = string.IsNullOrWhiteSpace(lang) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : lang.Trim();
        return code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? English : Turkish;
    }
}

/// <summary>A template column with its header in the requested language and every supported language (for auto-mapping).</summary>
public sealed record LocalizedImportColumn(ImportColumn Column, string Header, IReadOnlyList<string> HeaderAliases);

/// <summary>Registered templates/handlers plus localization and the template-level permission check.</summary>
public sealed class ImportTemplateCatalog(
    IEnumerable<IImportTemplate> templates,
    IEnumerable<IImportRowHandler> handlers,
    IImportLocalizer localizer,
    ICurrentUser currentUser,
    IPermissionService permissions)
{
    private const string PermissionArg = "permission";

    public IReadOnlyList<IImportTemplate> All { get; } = templates.OrderBy(t => t.Code, StringComparer.Ordinal).ToList();

    public Result<IImportTemplate> Find(string code)
    {
        var template = All.FirstOrDefault(t => string.Equals(t.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        return template is null ? Error.NotFound(DataTransferErrors.TemplateNotFound) : Result.Success(template);
    }

    public Result<IImportRowHandler> FindHandler(string code)
    {
        var handler = handlers.FirstOrDefault(h => string.Equals(h.TemplateCode, code, StringComparison.OrdinalIgnoreCase));
        return handler is null ? Error.NotFound(DataTransferErrors.TemplateNotFound) : Result.Success(handler);
    }

    /// <summary>The template's module permission (e.g. employee.employees.import) on top of the request's datatransfer permission.</summary>
    public async Task<Result> AuthorizeAsync(IImportTemplate template, CancellationToken ct)
    {
        // An impersonation session was already validated by AuthorizationBehaviour for this request.
        if (currentUser.ClaimsPlatformAccess())
        {
            return Result.Success();
        }

        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(Shared.Kernel.Results.ErrorCodes.Unauthenticated);
        }

        return await permissions.HasAsync(userId, template.RequiredPermission, ct).ConfigureAwait(false)
            ? Result.Success()
            : Error.Forbidden(Shared.Kernel.Results.ErrorCodes.Forbidden, (PermissionArg, template.RequiredPermission));
    }

    public async Task<IReadOnlyList<LocalizedImportColumn>> GetColumnsAsync(IImportTemplate template, CultureInfo culture, CancellationToken ct)
    {
        var columns = await template.GetColumnsAsync(ct).ConfigureAwait(false);
        return columns.Select(c => new LocalizedImportColumn(
                c,
                HeaderOf(c, culture),
                ImportCultures.Supported.Select(s => HeaderOf(c, s)).Distinct().ToList()))
            .ToList();
    }

    public string HeaderOf(ImportColumn column, CultureInfo culture) =>
        column.HeaderKey is { } key ? localizer.Get(key, culture) : column.HeaderText ?? column.Key;

    public string Text(string key, CultureInfo culture, IReadOnlyDictionary<string, string>? args = null) => localizer.Get(key, culture, args);

    public ImportTemplateDto ToDto(IImportTemplate template, IReadOnlyList<LocalizedImportColumn> columns, CultureInfo culture) => new(
        template.Code,
        localizer.Get(template.NameKey, culture),
        localizer.Get(template.DescriptionKey, culture),
        template.SupportedModes.Select(m => m.ToString()).ToList(),
        columns.Select(c => new ImportColumnDto(
            c.Column.Key, c.Header, c.Column.DataType.ToString(), c.Column.Requirement.ToString(), c.Column.IsMatchKey, c.Column.Options)).ToList());

    public string RowErrorMessage(ImportJobRowError error, CultureInfo culture) => localizer.Get(error.Code, culture, error.Args);
}

/// <summary>Detects the template key row and suggests a target column for every source column.</summary>
public static class ColumnAutoMapper
{
    public const int KeyRowNumber = 2;

    /// <summary>Row 2 is the key row when at least half of its non-empty cells are template column keys.</summary>
    public static bool IsKeyRow(SpreadsheetRow? row, IEnumerable<string> templateKeys)
    {
        if (row is null || row.RowNumber != KeyRowNumber)
        {
            return false;
        }

        var keys = templateKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = row.Cells.Where(c => !c.IsEmpty).Select(c => c.Text!.Trim()).ToList();
        var matches = values.Count(keys.Contains);
        return matches > 0 && matches * 2 >= values.Count;
    }

    /// <summary>Auto-mapping order: key row cell, then header text in any supported language (case/whitespace-insensitive). Each target is used once.</summary>
    public static IReadOnlyList<(string? TargetKey, bool AutoMatched)> Suggest(
        IReadOnlyList<string> headers, SpreadsheetRow? keyRow, int columnCount, IReadOnlyList<LocalizedImportColumn> columns)
    {
        var suggestions = new (string? TargetKey, bool AutoMatched)[columnCount];
        var used = new HashSet<string>(StringComparer.Ordinal);

        if (keyRow is not null)
        {
            for (var i = 0; i < columnCount; i++)
            {
                var keyText = keyRow.Cell(i).Text?.Trim();
                var match = columns.FirstOrDefault(c => string.Equals(c.Column.Key, keyText, StringComparison.OrdinalIgnoreCase));
                if (match is not null && used.Add(match.Column.Key))
                {
                    suggestions[i] = (match.Column.Key, true);
                }
            }
        }

        for (var i = 0; i < columnCount; i++)
        {
            if (suggestions[i].TargetKey is not null || i >= headers.Count || string.IsNullOrWhiteSpace(headers[i]))
            {
                continue;
            }

            var match = columns.FirstOrDefault(c => !used.Contains(c.Column.Key) && c.HeaderAliases.Any(h => ImportValueParser.TextEquals(h, headers[i])));
            if (match is not null && used.Add(match.Column.Key))
            {
                suggestions[i] = (match.Column.Key, true);
            }
        }

        return suggestions;
    }

    /// <summary>Source columns with up to three sample values; fully empty columns (no header, key or data) are left out.</summary>
    public static IReadOnlyList<ImportSourceColumn> BuildSourceColumns(Spreadsheet sheet, bool hasKeyRow, IReadOnlyList<LocalizedImportColumn> columns)
    {
        var keyRow = hasKeyRow ? sheet.Rows.FirstOrDefault(r => r.RowNumber == KeyRowNumber) : null;
        var dataRows = DataRows(sheet, hasKeyRow);
        var count = sheet.ColumnCount;
        var suggestions = Suggest(sheet.Headers, keyRow, count, columns);

        var result = new List<ImportSourceColumn>(count);
        for (var i = 0; i < count; i++)
        {
            var header = i < sheet.Headers.Count ? sheet.Headers[i].Trim() : string.Empty;
            var samples = dataRows.Select(r => r.Cell(i)).Where(c => !c.IsEmpty).Take(Shared.Contracts.Import.ImportLimits.SampleCount).Select(c => c.Text ?? string.Empty).ToList();
            if (header.Length == 0 && samples.Count == 0 && suggestions[i].TargetKey is null)
            {
                continue;
            }

            result.Add(new ImportSourceColumn(i, header, samples, suggestions[i].TargetKey, suggestions[i].AutoMatched));
        }

        return result;
    }

    public static IReadOnlyList<SpreadsheetRow> DataRows(Spreadsheet sheet, bool hasKeyRow) =>
        hasKeyRow ? sheet.Rows.Where(r => r.RowNumber != KeyRowNumber).ToList() : sheet.Rows;
}
