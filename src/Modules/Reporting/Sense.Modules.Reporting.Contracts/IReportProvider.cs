using System.Globalization;

namespace Sense.Modules.Reporting.Contracts;

public sealed record ReportFileResult(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Raporlar menüsü kategorileri (Kolay İK: İdari / Çalışan / İzinler / Finans - 11.3.12 "Raporlar menüsü yapısı").
/// Görünen ad web tarafında <c>reports:categories.{key}</c> anahtarıyla çözülür; sıralama <see cref="All"/> sırasıdır.
/// </summary>
public static class ReportCategories
{
    public const string Administrative = "administrative";
    public const string Employee = "employee";
    public const string Leave = "leave";
    public const string Finance = "finance";

    /// <summary>Zaman &amp; devam reports (daily attendance, late/early, overtime).</summary>
    public const string Time = "time";

    public static readonly IReadOnlyList<string> All = [Administrative, Employee, Leave, Finance, Time];

    /// <summary>Katalog sıralaması için; bilinmeyen kategori sona düşer.</summary>
    public static int SortOrder(string categoryKey)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i], categoryKey, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return All.Count;
    }
}

/// <summary>
/// Bir raporun kabul ettiği filtre türü. Web bu türe göre uygun kontrolü çizer (birim ağacı seçici, yıl/ay seçimi,
/// tarih aralığı, anahtar). Değerler <see cref="IReportProvider.GenerateAsync"/>'e her zaman string sözlüğüyle gelir;
/// biçim kuralları <see cref="ReportParameters"/>'ta.
/// </summary>
public enum ReportParameterKind
{
    /// <summary>Kolay İK "Birimler": virgülle ayrılmış OrgUnit id listesi; seçilen birim + alt birimleri kapsanır.</summary>
    OrgUnits = 0,

    /// <summary>Kolay İK "Yıl": dört haneli yıl.</summary>
    Year = 1,

    /// <summary>Kolay İK "Ay": 1–12.</summary>
    Month = 2,

    /// <summary>Tarih aralığı: <see cref="ReportParameterKeys.From"/> ve <see cref="ReportParameterKeys.To"/> anahtarları (yyyy-MM-dd).</summary>
    DateRange = 3,

    /// <summary>"true"/"false" anahtarı (örn. işten ayrılanları dahil et).</summary>
    Boolean = 4,

    /// <summary>Tek seçimli liste: değer <see cref="ReportParameterDescriptor.Options"/>'tan biri olmalıdır (örn. günlük durum, çıktı biçimi).</summary>
    Select = 5,
}

/// <summary>Bir raporun tek bir filtresi. <see cref="ReportParameterKind.DateRange"/> için <paramref name="Key"/> mantıksal addır, gerçek anahtarlar from/to'dur.</summary>
/// <param name="Options"><see cref="ReportParameterKind.Select"/> için izinli değerler (web etiketi <c>reports:params.{Key}Options.{değer}</c>); diğer türlerde null.</param>
public sealed record ReportParameterDescriptor(string Key, ReportParameterKind Kind, bool IsRequired = false, IReadOnlyList<string>? Options = null);

/// <summary>Sağlayıcıların ortak kullandığı parametre anahtarları (web ile paylaşılan sözleşme).</summary>
public static class ReportParameterKeys
{
    public const string OrgUnitIds = "orgUnitIds";
    public const string Year = "year";
    public const string Month = "month";
    public const string DateRange = "dateRange";
    public const string From = "from";
    public const string To = "to";
    public const string IncludeTerminated = "includeTerminated";

    /// <summary>Single date (yyyy-MM-dd), e.g. the day of a daily attendance report.</summary>
    public const string Date = "date";
}

/// <summary>
/// Parametre sözlüğünü okuma/doğrulama yardımcıları. Reporting'in komut işleyicisi zorunlu/biçimsel doğrulamayı
/// burada yapar; sağlayıcılar aynı yardımcılarla değeri okur, böylece iki taraf aynı kuralı paylaşır.
/// </summary>
public static class ReportParameters
{
    private const string DateFormat = "yyyy-MM-dd";

    public static bool Has(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    /// <summary>Virgülle ayrılmış GUID listesi; boş/eksik → boş liste. Geçersiz bir öğe varsa false döner.</summary>
    public static bool TryGetGuidList(IReadOnlyDictionary<string, string> parameters, string key, out IReadOnlyList<Guid> ids)
    {
        var list = new List<Guid>();
        ids = list;
        if (!Has(parameters, key))
        {
            return true;
        }

        foreach (var part in parameters[key].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id))
            {
                return false;
            }

            list.Add(id);
        }

        return true;
    }

    public static IReadOnlyList<Guid> GetGuidList(IReadOnlyDictionary<string, string> parameters, string key) =>
        TryGetGuidList(parameters, key, out var ids) ? ids : [];

    public static bool TryGetInt(IReadOnlyDictionary<string, string> parameters, string key, out int? value)
    {
        value = null;
        if (!Has(parameters, key))
        {
            return true;
        }

        if (!int.TryParse(parameters[key].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    public static int? GetInt(IReadOnlyDictionary<string, string> parameters, string key) =>
        TryGetInt(parameters, key, out var value) ? value : null;

    public static bool TryGetDate(IReadOnlyDictionary<string, string> parameters, string key, out DateOnly? value)
    {
        value = null;
        if (!Has(parameters, key))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(parameters[key].Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    public static DateOnly? GetDate(IReadOnlyDictionary<string, string> parameters, string key) =>
        TryGetDate(parameters, key, out var value) ? value : null;

    /// <summary>from/to çifti; ikisi de doluysa ve from ≤ to ise aralık döner, aksi halde null.</summary>
    public static (DateOnly From, DateOnly To)? GetDateRange(IReadOnlyDictionary<string, string> parameters)
    {
        var from = GetDate(parameters, ReportParameterKeys.From);
        var to = GetDate(parameters, ReportParameterKeys.To);
        return from is { } f && to is { } t && f <= t ? (f, t) : null;
    }

    public static bool TryGetBool(IReadOnlyDictionary<string, string> parameters, string key, out bool? value)
    {
        value = null;
        if (!Has(parameters, key))
        {
            return true;
        }

        if (!bool.TryParse(parameters[key].Trim(), out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    public static bool GetBool(IReadOnlyDictionary<string, string> parameters, string key, bool defaultValue = false) =>
        TryGetBool(parameters, key, out var value) && value is { } v ? v : defaultValue;

    /// <summary>
    /// Kolay İK Ay/Yıl filtre semantiği (11.3.12 "Filtre semantiği"): filtre yok → null; yalnız Yıl → o yılın Aralık
    /// ayı; Ay + Yıl → ayın ilk–son günü; yalnız Ay → cari yılın o ayı.
    /// </summary>
    public static (DateOnly From, DateOnly To)? ResolveMonthYearRange(IReadOnlyDictionary<string, string> parameters, DateOnly today)
    {
        var year = GetInt(parameters, ReportParameterKeys.Year);
        var month = GetInt(parameters, ReportParameterKeys.Month);
        if (year is null && month is null)
        {
            return null;
        }

        var y = year ?? today.Year;
        var m = month ?? 12;
        if (m is < 1 or > 12 || y is < 1900 or > 9999)
        {
            return null;
        }

        var from = new DateOnly(y, m, 1);
        return (from, new DateOnly(y, m, DateTime.DaysInMonth(y, m)));
    }
}

/// <summary>
/// Raporlar menüsünde listelenen, herhangi bir modülün DI kaydıyla katkı verdiği tek bir hazır rapor (Kolay İK
/// "rapor türü"). Reporting tüm kayıtlı sağlayıcıları (<c>IEnumerable&lt;IReportProvider&gt;</c>) sayarak kataloğu
/// kurar ve üretimi dağıtır; sağlayan modül yalnızca bu Contracts projesine referans verir, tersi olmaz
/// (Modules_TalkOnlyThroughContracts). Sağlayıcı, çağıranın veri kapsamını (<c>IDataScopeService</c>) kendisi
/// uygular - Reporting hangi çalışanların görünür olduğunu bilmez.
/// </summary>
public interface IReportProvider
{
    /// <summary>Kararlı anahtar, örn. "leave.balance.summary"; web görünen adı/açıklamayı "reports" i18n alanından bu anahtarla çözer.</summary>
    string Key { get; }

    /// <summary>sync (default) or async; background reports return a queued execution.</summary>
    string ExecutionMode => "sync";

    /// <summary>Menü kategorisi; <see cref="ReportCategories"/> anahtarlarından biri.</summary>
    string CategoryKey { get; }

    /// <summary>Temel <c>reports.*</c> kapısının ötesinde çağıranın taşıması gereken ek izin; null = yok.</summary>
    string? RequiredPermission { get; }

    /// <summary>Çalışan veri kapsamı görüntüsünü üretirken kullanılacak izin; null = sağlayıcı kapsamı kendisi çözer.</summary>
    string? DataScopePermission => RequiredPermission;

    /// <summary>Raporun kabul ettiği filtreler; boş = filtresiz. Web bu listeden parametre formunu çizer.</summary>
    IReadOnlyList<ReportParameterDescriptor> Parameters => [];

    Task<ReportFileResult> GenerateAsync(Guid tenantId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}
