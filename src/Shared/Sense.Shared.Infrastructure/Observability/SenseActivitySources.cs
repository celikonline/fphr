using System.Diagnostics;

namespace Sense.Shared.Infrastructure.Observability;

/// <summary>
/// Kritik iş operasyonları için özel OpenTelemetry ActivitySource'ları. Dispatcher zaten her istek için
/// <see cref="Sense.Shared.Contracts.Configuration.DiagnosticsDefaults.ActivitySourceName"/> altında span açıyor;
/// bunlar dispatcher dışında (dosya depolama, arka plan işleri) manuel span açmak için kullanılır.
/// OpenTelemetryExtensions.AddSenseTracing bu kaynakları <c>AddSource(...)</c> ile dinlemeye alır.
/// </summary>
public static class SenseActivitySources
{
    public const string DocumentsName = "Sense.Documents";
    public const string BackgroundJobsName = "Sense.BackgroundJobs";

    public static readonly ActivitySource Documents = new(DocumentsName);
    public static readonly ActivitySource BackgroundJobs = new(BackgroundJobsName);
}
