using Sense.Shared.Contracts.Export;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Contracts.CardPdf;

/// <summary>Employee card tab keys (same as the web card's URL segments), in PDF display order.</summary>
public static class EmployeeCardPdfSectionKeys
{
    public const string General = "general";
    public const string Personal = "personal";
    public const string CustomFields = "customFields";
    public const string Career = "career";
    public const string Payments = "payments";
    public const string Payroll = "payroll";
    public const string Documents = "documents";
    public const string Assets = "assets";
    public const string Leave = "leave";
    public const string Overtime = "overtime";
    public const string Account = "account";

    public static readonly IReadOnlyList<string> All =
        [General, Personal, CustomFields, Career, Payments, Payroll, Documents, Assets, Leave, Overtime, Account];

    /// <summary>Turkish section title printed on the section band.</summary>
    public static string Title(string key) => key switch
    {
        General => "Genel Bilgiler",
        Personal => "Kişisel Bilgiler",
        CustomFields => "Özel Alanlar",
        Career => "Kariyer",
        Payments => "Ödemeler",
        Payroll => "Bordro",
        Documents => "Evraklar",
        Assets => "Zimmetler",
        Leave => "İzinler",
        Overtime => "Fazla Mesai",
        Account => "Hesap",
        _ => key,
    };
}

/// <summary>
/// One card PDF request. <paramref name="IsOwnCard"/>: the caller is the employee (self-service), so providers use the
/// module's "my" queries. <paramref name="From"/>/<paramref name="To"/> filter event-type lists only; null = unbounded.
/// </summary>
public sealed record EmployeeCardPdfRequest(Guid TenantId, Guid EmployeeId, bool IsOwnCard, DateOnly? From, DateOnly? To);

/// <summary>A provider's output: blocks to print, or <c>null</c> blocks when the caller may not see this section.</summary>
public sealed record EmployeeCardPdfSectionResult(IReadOnlyList<SimplePdfBlock>? Blocks)
{
    public static readonly EmployeeCardPdfSectionResult Unavailable = new((IReadOnlyList<SimplePdfBlock>?)null);

    public bool IsAvailable => Blocks is not null;

    /// <summary>
    /// Maps one query result to a part: success → blocks, Forbidden/NotFound → null (hidden from this caller);
    /// any other failure throws so the whole PDF fails instead of silently missing data.
    /// </summary>
    public static IReadOnlyList<SimplePdfBlock>? Part<T>(Result<T> result, Func<T, IEnumerable<SimplePdfBlock>> map)
    {
        if (result.IsSuccess)
        {
            return map(result.Value).ToList();
        }

        return result.Error.Type is ErrorType.Forbidden or ErrorType.NotFound
            ? null
            : throw new InvalidOperationException($"Employee card PDF section query failed: {result.Error.Code}");
    }

    /// <summary>Unavailable only when every part is hidden; otherwise the visible parts in order.</summary>
    public static EmployeeCardPdfSectionResult Combine(params IReadOnlyList<SimplePdfBlock>?[] parts) =>
        parts.All(part => part is null)
            ? Unavailable
            : new EmployeeCardPdfSectionResult(parts.Where(part => part is not null).SelectMany(part => part!).ToList());
}

/// <summary>
/// Contributes one employee card tab to the card PDF. Implemented by the module that owns the tab's data (in its
/// Infrastructure) and registered in its module; Employee collects every registration. Providers sharing a
/// <see cref="Key"/> are joined by <see cref="Order"/>.
/// </summary>
public interface IEmployeeCardPdfSection
{
    string Key { get; }

    int Order { get; }

    /// <summary>ProductModuleKeys value the provider needs enabled; null for core modules.</summary>
    string? ModuleKey { get; }

    Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken);
}
