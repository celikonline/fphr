using System.Reflection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.Observability;

namespace Sense.Api.Observability;

/// <summary>
/// OpenTelemetry dağıtık izleme (tracing) ve metrik kurulumu. Jaeger'in kendi (deprecated) exporter'ı yerine
/// OTLP/gRPC exporter'ı Jaeger'in yerel OTLP alıcısına (varsayılan http://localhost:4317) gönderir
/// (bkz. infra/docker-compose.yml "jaeger" servisi, profile "obs").
/// </summary>
public static class OpenTelemetryExtensions
{
    private static readonly string ServiceVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    public static IServiceCollection AddSenseObservability(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(ConfigurationSections.Observability).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        var otel = services.AddOpenTelemetry().ConfigureResource(r => r
            .AddService(options.ServiceName, serviceVersion: ServiceVersion)
            .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)]));

        if (options.EnableTracing)
        {
            otel.WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(o =>
                {
                    // Health check probe trafiği trace gürültüsü yaratmasın (kısa aralıklarla, çok sayıda çağrı).
                    o.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments(HostConstants.HealthPath);
                })
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource(DiagnosticsDefaults.ActivitySourceName)
                .AddSource(SenseActivitySources.DocumentsName)
                .AddSource(SenseActivitySources.BackgroundJobsName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)));
        }

        if (options.EnableMetrics)
        {
            otel.WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)));
        }

        return services;
    }
}
