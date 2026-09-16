namespace Sense.Shared.Contracts.Configuration;

/// <summary>appsettings bölüm adları.</summary>
public static class ConfigurationSections
{
    public const string Paging = "Paging";
    public const string Localization = "Localization";
    public const string Caching = "Caching";
    public const string Outbox = "Outbox";
    public const string Diagnostics = "Diagnostics";
    public const string ProblemDetails = "ProblemDetails";
    public const string Auth = "Auth";
    public const string Storage = "Storage";
    public const string Email = "Email";
    public const string Frontend = "Frontend";
    public const string Identity = "Identity";
    public const string Observability = "Observability";
    public const string RateLimiting = "RateLimiting";
}

public static class ConnectionStringNames
{
    public const string Database = "Database";
    public const string Redis = "Redis";
}

/// <summary>Liste uçlarının sayfalama sınırları.</summary>
public sealed class PagingOptions
{
    public int DefaultPageSize { get; set; } = PagingDefaults.DefaultPageSize;

    public int MaxPageSize { get; set; } = PagingDefaults.MaxPageSize;

    /// <summary>Bu satır sayısının üstündeki dışa aktarmalar arka plan işine alınır.</summary>
    public int SyncExportRowLimit { get; set; } = PagingDefaults.SyncExportRowLimit;
}

public static class PagingDefaults
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;
    public const int SyncExportRowLimit = 5000;
    public const int FirstPage = 1;
}

/// <summary>Desteklenen kültürler; varsayılan tr-TR.</summary>
public sealed class LocalizationOptions
{
    public string DefaultCulture { get; set; } = Cultures.Turkish;

    public IList<string> SupportedCultures { get; set; } = [Cultures.Turkish, Cultures.English];

    public string ResourcesPath { get; set; } = LocalizationDefaults.ResourcesPath;
}

public static class Cultures
{
    public const string Turkish = "tr-TR";
    public const string English = "en-US";
    public const string TurkishLanguage = "tr";
    public const string EnglishLanguage = "en";
}

public static class LocalizationDefaults
{
    public const string ResourcesPath = "Resources";
}

public sealed class CachingOptions
{
    public int DefaultExpirationMinutes { get; set; } = CachingDefaults.DefaultExpirationMinutes;

    public int LocalExpirationSeconds { get; set; } = CachingDefaults.LocalExpirationSeconds;

    public int PermissionExpirationMinutes { get; set; } = CachingDefaults.PermissionExpirationMinutes;

    public string KeyPrefixGlobal { get; set; } = CachingDefaults.GlobalPrefix;
}

public static class CachingDefaults
{
    public const int DefaultExpirationMinutes = 5;
    public const int LocalExpirationSeconds = 30;
    public const int PermissionExpirationMinutes = 10;
    public const string GlobalPrefix = "global";
    public const char KeySeparator = ':';
}

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = OutboxDefaults.BatchSize;

    public int MaxAttempts { get; set; } = OutboxDefaults.MaxAttempts;

    public int BaseBackoffSeconds { get; set; } = OutboxDefaults.BaseBackoffSeconds;

    public int MaxBackoffSeconds { get; set; } = OutboxDefaults.MaxBackoffSeconds;

    public int PollingIntervalSeconds { get; set; } = OutboxDefaults.PollingIntervalSeconds;

    public int RetentionDays { get; set; } = OutboxDefaults.RetentionDays;
}

public static class OutboxDefaults
{
    public const int BatchSize = 50;
    public const int MaxAttempts = 10;
    public const int BaseBackoffSeconds = 5;
    public const int MaxBackoffSeconds = 3600;
    public const int PollingIntervalSeconds = 2;
    public const int RetentionDays = 30;
    public const int MaxErrorLength = 4000;
}

public sealed class DiagnosticsOptions
{
    public int SlowRequestThresholdMs { get; set; } = DiagnosticsDefaults.SlowRequestThresholdMs;

    public string ActivitySourceName { get; set; } = DiagnosticsDefaults.ActivitySourceName;
}

public static class DiagnosticsDefaults
{
    public const int SlowRequestThresholdMs = 500;
    public const string ActivitySourceName = "Sense.Dispatcher";
}

public sealed class ProblemMappingOptions
{
    /// <summary>ProblemDetails.type için taban adres; sonuna hata kodu eklenir.</summary>
    public string TypeBaseUrl { get; set; } = ProblemDetailsDefaults.TypeBaseUrl;

    public bool IncludeExceptionDetails { get; set; }
}

public static class ProblemDetailsDefaults
{
    public const string TypeBaseUrl = "https://docs.senseik.com/errors/";
    public const string CodeExtension = "code";
    public const string TraceIdExtension = "traceId";
    public const string ErrorsExtension = "errors";
    public const string ArgsExtension = "args";
}

/// <summary>
/// Gözlemlenebilirlik ayarları: OpenTelemetry (Jaeger/OTLP) ve Sentry. SentryDsn appsettings'te boş bırakılmalı;
/// üretimde <c>Observability__SentryDsn</c> ortam değişkeni veya <c>SENTRY_DSN</c> üzerinden sağlanır
/// (bkz. SentryExtensions.ResolveDsn). Boşsa Sentry devre dışı kalır (özellikle local dev'de gürültü istenmez).
/// </summary>
public sealed class ObservabilityOptions
{
    public string ServiceName { get; set; } = ObservabilityDefaults.ServiceName;

    /// <summary>OTLP collector/Jaeger uç noktası (gRPC), örn. http://localhost:4317.</summary>
    public string OtlpEndpoint { get; set; } = ObservabilityDefaults.OtlpEndpoint;

    /// <summary>Boş bırakılırsa Sentry devre dışıdır; asıl değer SENTRY_DSN ortam değişkeninden okunur.</summary>
    public string SentryDsn { get; set; } = string.Empty;

    public bool EnableTracing { get; set; } = true;

    /// <summary>Kök (parent'sız) izlerin örneklenme oranı (0.0-1.0); üst servisten gelen örnekleme kararına her zaman uyulur.</summary>
    public double TracingSampleRatio { get; set; } = ObservabilityDefaults.TracingSampleRatio;

    public bool EnableMetrics { get; set; } = true;

    /// <summary>Prometheus'un scrape ettiği uç nokta (OpenTelemetry metrikleri Prometheus metin formatında).</summary>
    public string MetricsPath { get; set; } = ObservabilityDefaults.MetricsPath;

    /// <summary>Sentry performans izleme örnekleme oranı (0.0-1.0); hata yakalama her zaman 1.0'dır.</summary>
    public double SentryTracesSampleRate { get; set; } = ObservabilityDefaults.SentryTracesSampleRate;

    public LogExportOptions Logs { get; set; } = new();
}

/// <summary>Serilog → OTLP/HTTP log aktarımı (varsayılan hedef: Loki'nin yerel OTLP alıcısı, Grafana'dan sorgulanır).</summary>
public sealed class LogExportOptions
{
    public bool Enabled { get; set; }

    /// <summary>Tam OTLP logs URL'i; Loki için <c>http://{host}:3100/otlp/v1/logs</c>.</summary>
    public string Endpoint { get; set; } = ObservabilityDefaults.LogsEndpoint;
}

public static class ObservabilityDefaults
{
    public const string ServiceName = "SenseIK.Api";
    public const string OtlpEndpoint = "http://localhost:4317";
    public const string LogsEndpoint = "http://localhost:3100/otlp/v1/logs";
    public const string MetricsPath = "/metrics";
    public const double TracingSampleRatio = 1.0;
    public const double SentryTracesSampleRate = 0.1;
}

/// <summary>
/// ADR-0003: anonim (kimlik doğrulamasız) uçlar için IP bazlı sabit pencere hız sınırlama. Politika adları
/// (<see cref="RateLimitPolicyNames.PublicAnonymous"/>/<see cref="RateLimitPolicyNames.PublicAnonymousWrite"/>)
/// sözleşme sayılır, değiştirilemez; sadece sayısal değerler burada ayarlanır.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Anonim okuma uçları (ör. GET) için politika.</summary>
    public RateLimitPolicyOptions PublicAnonymous { get; set; } = new() { PermitLimit = RateLimitingDefaults.PublicAnonymousPermitLimit };

    /// <summary>Anonim yazma uçları (ör. POST başvuru, şifre sıfırlama) için daha sıkı politika.</summary>
    public RateLimitPolicyOptions PublicAnonymousWrite { get; set; } = new() { PermitLimit = RateLimitingDefaults.PublicAnonymousWritePermitLimit };
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = RateLimitingDefaults.PublicAnonymousPermitLimit;

    public int WindowSeconds { get; set; } = RateLimitingDefaults.WindowSeconds;

    public int QueueLimit { get; set; }
}

public static class RateLimitingDefaults
{
    public const int PublicAnonymousPermitLimit = 30;
    public const int PublicAnonymousWritePermitLimit = 5;
    public const int WindowSeconds = 60;
}

/// <summary>Sözleşme: politika adları PO sahipliğinde, değiştirilemez (bkz. ADR-0003).</summary>
public static class RateLimitPolicyNames
{
    public const string PublicAnonymous = "public-anonymous";
    public const string PublicAnonymousWrite = "public-anonymous-write";
}
