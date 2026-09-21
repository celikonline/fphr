using System.Reflection;

namespace Sense.Shared.Contracts.Modules;

/// <summary>
/// Ürün modülü kataloğu girdisi (senseik-057 "Ayarlar → Modüller").
/// <paramref name="Key"/> = API/ön yüz anahtarı (camelCase, kalıcı: kiracının kapalı modül listesinde saklanır);
/// <paramref name="AssemblySegment"/> = <c>Sense.Modules.{Segment}.Api</c> assembly adındaki modül klasörü;
/// <paramref name="IsToggleable"/> = kiracı yöneticisi kapatabilir mi (false = "temel modül", her zaman açık).
/// </summary>
public sealed record ProductModule(string Key, string AssemblySegment, bool IsToggleable);

/// <summary>Kiracı bazında aç/kapa edilebilen ürün modüllerinin anahtarları (kalıcı değerler — yeniden adlandırmayın).</summary>
public static class ProductModuleKeys
{
    public const string Identity = "identity";
    public const string Organization = "organization";
    public const string Employee = "employee";
    public const string Workflow = "workflow";
    public const string Notifications = "notifications";
    public const string Documents = "documents";
    public const string DataTransfer = "dataTransfer";
    public const string Reporting = "reporting";
    public const string Analytics = "analytics";
    public const string Integrations = "integrations";

    public const string Leave = "leave";
    public const string Attendance = "attendance";
    public const string Payroll = "payroll";
    public const string Recruitment = "recruitment";
    public const string Expense = "expense";
    public const string Assets = "assets";
    public const string Performance = "performance";
    public const string Announcements = "announcements";
    public const string OccupationalSafety = "occupationalSafety";
    public const string Training = "training";
}

/// <summary>
/// Tek modül kataloğu: hangi modüller temel (kapatılamaz), hangileri kiracı tarafından kapatılabilir.
/// Temel modüller diğerlerinin bağımlı olduğu çekirdektir (kimlik/organizasyon/çalışan kaydı, onay akışı, bildirim,
/// doküman, veri aktarımı, raporlama/analitik, entegrasyonlar); kapatılabilir modüller yalnız Contracts üzerinden
/// okunur, bu yüzden kapatılmaları çekirdek akışları bozmaz. Kapalı bir modülün HTTP uçları
/// <c>ModuleAccessFilter</c> ile 403 döner; arka plan işleri/olay handler'ları çalışmaya devam eder (veri tutarlılığı).
/// Yeni bir modül eklendiğinde buraya da eklenmeli; listede olmayan bir modülün uçları hiçbir zaman engellenmez.
/// </summary>
public static class ProductModules
{
    private const string AssemblyPrefix = "Sense.Modules.";

    public static IReadOnlyList<ProductModule> All { get; } =
    [
        new(ProductModuleKeys.Identity, "Identity", IsToggleable: false),
        new(ProductModuleKeys.Organization, "Organization", IsToggleable: false),
        new(ProductModuleKeys.Employee, "Employee", IsToggleable: false),
        new(ProductModuleKeys.Workflow, "Workflow", IsToggleable: false),
        new(ProductModuleKeys.Notifications, "Notifications", IsToggleable: false),
        new(ProductModuleKeys.Documents, "Documents", IsToggleable: false),
        new(ProductModuleKeys.DataTransfer, "DataTransfer", IsToggleable: false),
        new(ProductModuleKeys.Reporting, "Reporting", IsToggleable: false),
        new(ProductModuleKeys.Analytics, "Analytics", IsToggleable: false),
        new(ProductModuleKeys.Integrations, "Integrations", IsToggleable: false),

        new(ProductModuleKeys.Leave, "Leave", IsToggleable: true),
        new(ProductModuleKeys.Attendance, "Attendance", IsToggleable: true),
        new(ProductModuleKeys.Payroll, "Payroll", IsToggleable: true),
        new(ProductModuleKeys.Recruitment, "Recruitment", IsToggleable: true),
        new(ProductModuleKeys.Expense, "Expense", IsToggleable: true),
        new(ProductModuleKeys.Assets, "Assets", IsToggleable: true),
        new(ProductModuleKeys.Performance, "Performance", IsToggleable: true),
        new(ProductModuleKeys.Announcements, "Announcements", IsToggleable: true),
        new(ProductModuleKeys.OccupationalSafety, "OccupationalSafety", IsToggleable: true),
        new(ProductModuleKeys.Training, "Training", IsToggleable: true),
    ];

    private static readonly Dictionary<string, ProductModule> ByKey = All.ToDictionary(m => m.Key, StringComparer.Ordinal);

    private static readonly Dictionary<string, ProductModule> BySegment = All.ToDictionary(m => m.AssemblySegment, StringComparer.Ordinal);

    public static ProductModule? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var module) ? module : null;

    public static bool IsToggleable(string? key) => Find(key)?.IsToggleable == true;

    /// <summary><c>Sense.Modules.Leave.Api</c> → leave modülü; modül assembly'si değilse veya katalogda yoksa null.</summary>
    public static ProductModule? FromAssemblyName(string? assemblyName)
    {
        if (assemblyName is null || !assemblyName.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = assemblyName[AssemblyPrefix.Length..];
        var end = rest.IndexOf('.', StringComparison.Ordinal);
        var segment = end < 0 ? rest : rest[..end];
        return BySegment.GetValueOrDefault(segment);
    }

    public static ProductModule? FromAssembly(Assembly assembly) => FromAssemblyName(assembly.GetName().Name);
}

/// <summary>Modül aç/kapa hata kodları (SharedResource{,.en}.resx'te yerelleştirilir).</summary>
public static class ProductModuleErrors
{
    /// <summary>403: istenen uç, kiracı için kapatılmış bir modüle ait.</summary>
    public const string Disabled = "module.disabled";

    /// <summary>400: katalogda olmayan modül anahtarı.</summary>
    public const string Unknown = "module.unknown";

    /// <summary>400: temel (kapatılamaz) modül kapatılmaya çalışıldı.</summary>
    public const string NotToggleable = "module.not_toggleable";

    /// <summary>Hata mesajı şablonundaki modül anahtarı parametresi: {module}.</summary>
    public const string ModuleArg = "module";
}

/// <summary>
/// Mevcut kiracının modül aç/kapa durumu (Identity modülü uygular, HybridCache ile önbellekli).
/// Kiracı bağlamı yoksa (anonim uçlar, arka plan işleri) her modül açık sayılır.
/// </summary>
public interface IModuleAccessService
{
    /// <summary>Mevcut kiracının kapattığı modül anahtarları (boş = hepsi açık).</summary>
    Task<IReadOnlyCollection<string>> GetDisabledModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Temel modüller ve katalogda olmayan anahtarlar için her zaman true.</summary>
    Task<bool> IsEnabledAsync(string moduleKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bu controller/action, modülü kapalı olsa bile erişilebilir kalır (ör. Announcements assembly'sindeki destek talepleri).
/// <c>ModuleAccessFilter</c> bu işaretli uçları atlar.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ModuleAccessExemptAttribute : Attribute;
