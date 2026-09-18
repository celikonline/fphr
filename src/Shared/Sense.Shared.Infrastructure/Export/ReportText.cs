using Microsoft.Extensions.Localization;

namespace Sense.Shared.Infrastructure.Export;

/// <summary>
/// Localized text for report files (Excel column headers, sheet names, enum values) - the <see cref="IReportExporter"/>
/// contract takes already-localized headers, so every report provider resolves them here with the request culture
/// (Accept-Language → RequestLocalization). Keys live in SharedResource{,.en}.resx under <c>report.col.*</c>,
/// <c>report.sheet.*</c> and <c>report.enum.{EnumName}.{Value}</c>; a missing key falls back to the raw key/enum name
/// rather than throwing, so a new enum member never breaks a report.
/// </summary>
public static class ReportText
{
    private const string ColumnPrefix = "report.col.";
    private const string SheetPrefix = "report.sheet.";
    private const string EnumPrefix = "report.enum.";

    /// <summary>Column header, e.g. <c>Column(localizer, "firstName")</c> → "Ad" / "First name".</summary>
    public static string Column(this IStringLocalizer localizer, string key) => localizer[ColumnPrefix + key];

    /// <summary>Sheet/file base name, e.g. <c>Sheet(localizer, "employeeList")</c> → "Çalışan listesi".</summary>
    public static string Sheet(this IStringLocalizer localizer, string key) => localizer[SheetPrefix + key];

    /// <summary>Enum member text; null value → null; unknown key → the member name.</summary>
    public static string? EnumText<TEnum>(this IStringLocalizer localizer, TEnum? value) where TEnum : struct, Enum =>
        value is { } v ? localizer.EnumText(typeof(TEnum).Name, v.ToString()) : null;

    /// <summary>Enum member text from its C# name (for Contracts that carry enum names as strings).</summary>
    public static string? EnumText(this IStringLocalizer localizer, string enumName, string? memberName)
    {
        if (string.IsNullOrEmpty(memberName))
        {
            return null;
        }

        var resource = localizer[EnumPrefix + enumName + "." + memberName];
        return resource.ResourceNotFound ? memberName : resource.Value;
    }
}
