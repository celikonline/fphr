using Sense.Shared.Contracts.Configuration;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace Sense.Api.Observability;

/// <summary>
/// Serilog loglarını OTLP/HTTP ile Loki'nin yerel OTLP alıcısına (<c>/otlp/v1/logs</c>) gönderir; Grafana'da
/// "Loki" datasource'u üzerinden sorgulanır. Kaynak öznitelikleri Loki'de index etiketine dönüşür
/// (<c>service_name</c>, <c>deployment_environment</c>); log property'leri (CorrelationId, TenantId, UserId, ClientApp…)
/// ve <c>trace_id</c>/<c>span_id</c> structured metadata olarak saklanır — Grafana, <c>trace_id</c>'den Jaeger'deki ize bağlanır.
/// <c>Observability:Logs:Enabled=false</c> (varsayılan) iken hiçbir şey eklenmez.
/// </summary>
public static class LogExportExtensions
{
    public static LoggerConfiguration WriteToSenseLogExport(this LoggerConfiguration logger, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(ConfigurationSections.Observability).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        if (!options.Logs.Enabled)
        {
            return logger;
        }

        return logger.WriteTo.OpenTelemetry(
            o =>
            {
                o.LogsEndpoint = options.Logs.Endpoint;
                o.Protocol = OtlpProtocol.HttpProtobuf;
                o.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = options.ServiceName,
                    ["service.version"] = OpenTelemetryExtensions.ServiceVersion,
                    ["deployment.environment"] = environment.EnvironmentName,
                };
            },
            // Hedef yalnızca appsettings/ortam değişkeni "Observability__Logs__Endpoint" ile belirlenir; genel
            // OTEL_EXPORTER_OTLP_ENDPOINT (izler için Jaeger'i gösterebilir) logları yanlış yere yönlendirmesin.
            ignoreEnvironment: true);
    }
}
