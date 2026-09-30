using System.Globalization;
using System.Text;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Paging;

namespace Sense.Modules.Reporting.Application;

/// <summary>Okunmuş bir rapor sayfasını ekran için arama → sıralama → sayfalama sırasıyla hazırlar (bellekte; dosya zaten ayrıştırılmıştır).</summary>
public static class ReportTableView
{
    private static readonly StringComparer TextComparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    public static PagedResult<IReadOnlyList<object?>> Build(ExportTable table, string? search, int? sortColumn, bool descending, int page, int pageSize)
    {
        IEnumerable<IReadOnlyList<object?>> rows = table.Rows;

        var needle = Fold((search ?? string.Empty).Trim());
        if (needle.Length > 0)
        {
            rows = rows.Where(row => row.Any(cell => cell is not null && Fold(CellText(cell)).Contains(needle, StringComparison.Ordinal)));
        }

        if (sortColumn is { } column)
        {
            rows = Sort(rows, column, descending);
        }

        var matched = rows.ToList();
        var skip = (long)(page - 1) * pageSize;
        var items = matched.Skip(skip > int.MaxValue ? int.MaxValue : (int)skip).Take(pageSize).ToList();
        return new PagedResult<IReadOnlyList<object?>>(items, page, pageSize, matched.Count);
    }

    private static IEnumerable<IReadOnlyList<object?>> Sort(IEnumerable<IReadOnlyList<object?>> rows, int column, bool descending)
    {
        var list = rows.ToList();
        var withValue = list.Where(row => Cell(row, column) is not null);
        var withoutValue = list.Where(row => Cell(row, column) is null);
        var ordered = descending
            ? withValue.OrderByDescending(row => Cell(row, column), CellComparer.Instance)
            : withValue.OrderBy(row => Cell(row, column), CellComparer.Instance);
        return ordered.Concat(withoutValue);
    }

    private static object? Cell(IReadOnlyList<object?> row, int column) => column < row.Count ? row[column] : null;

    private static string CellText(object value) => value switch
    {
        string text => text,
        DateOnly date => $"{date:dd.MM.yyyy} {date:yyyy-MM-dd}",
        DateTime dateTime => $"{dateTime:dd.MM.yyyy HH:mm} {dateTime:yyyy-MM-dd HH:mm}",
        bool flag => flag ? "true" : "false",
        double number => number.ToString("0.############", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>Küçük harfe çevirir ve Türkçe I/İ/ı ile aksanları eşitler ("İzin" ↔ "izin", "Çalışan" ↔ "calisan"); web katalog aramasındaki <c>fold</c> ile aynı kural.</summary>
    private static string Fold(string value)
    {
        var lowered = value.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        foreach (var ch in lowered.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Sütun tek tipli olduğundan (okuyucu garanti eder) aynı tipleri kendi sırasıyla, metni Türkçe harf sırasıyla karşılaştırır.</summary>
    private sealed class CellComparer : IComparer<object?>
    {
        public static readonly CellComparer Instance = new();

        public int Compare(object? x, object? y) => (x, y) switch
        {
            (string a, string b) => TextComparer.Compare(a, b),
            (IComparable a, { } b) when a.GetType() == b.GetType() => a.CompareTo(b),
            _ => TextComparer.Compare(CellText(x!), CellText(y!)),
        };
    }
}
