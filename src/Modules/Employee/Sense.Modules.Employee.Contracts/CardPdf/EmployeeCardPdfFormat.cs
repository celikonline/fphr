using System.Globalization;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Employee.Contracts.CardPdf;

/// <summary>Shared tr-TR formatting and table rules for every card PDF provider.</summary>
public static class EmployeeCardPdfFormat
{
    public const int MaxRowsPerTable = 500;
    public const string Empty = "—";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Date(DateOnly? value) =>
        value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? Empty;

    // Not named "DateTime": a member with that name would shadow the System.DateTime type inside this class.
    public static string Timestamp(System.DateTime? value) =>
        value is { } v ? Date(DateOnly.FromDateTime(v)) : Empty;

    public static string Amount(decimal? value, string? currency = null) =>
        value is not { } v ? Empty : currency is null ? v.ToString("N2", Turkish) : $"{v.ToString("N2", Turkish)} {currency}";

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Empty : value;

    public static string YesNo(bool value) => value ? "Evet" : "Hayır";

    /// <summary>Record [start, end] overlaps [from, to]; null end = single-day record; null bound = unbounded.</summary>
    public static bool InPeriod(DateOnly start, DateOnly? end, DateOnly? from, DateOnly? to) =>
        (to is null || start <= to) && (from is null || (end ?? start) >= from);

    public static string PeriodText(DateOnly? from, DateOnly? to) =>
        from is null && to is null ? "Tüm zamanlar" : $"{Date(from)} – {Date(to)}";

    public static SimplePdfKeyValues KeyValues(string? heading, params (string Label, string Value)[] rows) =>
        new(heading, rows.Select(row => new KeyValuePair<string, string>(row.Label, row.Value)).ToList());

    /// <summary>A table capped at <see cref="MaxRowsPerTable"/> rows (caller orders newest first) plus a note for the rest.</summary>
    public static IEnumerable<SimplePdfBlock> Table(
        string heading,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyList<int>? rightAlignedColumns = null)
    {
        var all = rows.ToList();
        if (all.Count == 0)
        {
            return [new SimplePdfParagraph($"{heading}: kayıt yok.")];
        }

        var table = new SimplePdfTable(heading, columns, all.Take(MaxRowsPerTable).ToList(), rightAlignedColumns);
        return all.Count > MaxRowsPerTable
            ? [table, new SimplePdfParagraph($"{all.Count - MaxRowsPerTable} kayıt daha var; dönemi daraltın.")]
            : [table];
    }
}
