using System.Diagnostics.Metrics;

namespace Sense.Shared.Infrastructure.Observability;

/// <summary>
/// Uygulamaya özel OpenTelemetry metrikleri (<see cref="SenseActivitySources"/> ile aynı statik desen).
/// OpenTelemetryExtensions bu meter'ı <c>AddMeter(SenseMeters.Name)</c> ile dinler; Prometheus'ta
/// <c>sense_*</c> önekiyle görünür (ör. <c>sense_dispatcher_request_duration_seconds_bucket</c>).
/// Etiketler bilinçli olarak düşük kardinaliteli tutulur: istek tipi/modül/iş tipi gibi sınırlı kümeler —
/// kiracı, kullanıcı veya kayıt kimliği asla metrik etiketi olmaz (bunlar loglarda/izlerde aranır).
/// </summary>
public static class SenseMeters
{
    public const string Name = "Sense";

    private static readonly Meter Meter = new(Name);

    /// <summary>Saniye cinsinden süre histogramları için kova sınırları (5 ms – 30 s).</summary>
    private static readonly InstrumentAdvice<double> DurationAdvice = new()
    {
        HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 10, 30],
    };

    /// <summary>Dispatcher üzerinden geçen her command/query'nin süresi.</summary>
    public static readonly Histogram<double> DispatcherRequestDuration = Meter.CreateHistogram(
        "sense.dispatcher.request.duration",
        unit: "s",
        description: "Duration of commands and queries handled by the dispatcher.",
        tags: null,
        advice: DurationAdvice);

    /// <summary>Hangfire arka plan işlerinin süresi.</summary>
    public static readonly Histogram<double> BackgroundJobDuration = Meter.CreateHistogram(
        "sense.background_job.duration",
        unit: "s",
        description: "Duration of background job executions.",
        tags: null,
        advice: DurationAdvice);

    /// <summary>Outbox işleyicisinin mesaj sonuçları (processed / retried / dead_lettered).</summary>
    public static readonly Counter<long> OutboxMessages = Meter.CreateCounter<long>(
        "sense.outbox.messages",
        unit: "{message}",
        description: "Outbox messages handled by the outbox processor, by result.");

    public static class Tags
    {
        public const string Request = "sense.request";
        public const string RequestKind = "sense.request.kind";
        public const string Module = "sense.module";
        public const string Outcome = "sense.outcome";
        public const string JobType = "sense.job.type";
        public const string OutboxContext = "sense.outbox.context";
        public const string OutboxResult = "sense.outbox.result";
    }

    public static class Outcomes
    {
        public const string Success = "success";
        public const string Failure = "failure";
        public const string Exception = "exception";
    }

    public static class OutboxResults
    {
        public const string Processed = "processed";
        public const string Retried = "retried";
        public const string DeadLettered = "dead_lettered";
    }

    public static class RequestKinds
    {
        public const string Command = "command";
        public const string Query = "query";
    }

    private const string ModuleNamespacePrefix = "Sense.Modules.";
    private const string SharedModule = "shared";

    /// <summary><c>Sense.Modules.{Modül}.*</c> isim alanından modül adını çıkarır; modül dışı tipler için "shared".</summary>
    public static string ModuleOf(Type type)
    {
        var ns = type.Namespace;
        if (ns is null || !ns.StartsWith(ModuleNamespacePrefix, StringComparison.Ordinal))
        {
            return SharedModule;
        }

        var rest = ns.AsSpan(ModuleNamespacePrefix.Length);
        var dot = rest.IndexOf('.');
        return (dot < 0 ? rest : rest[..dot]).ToString();
    }
}
