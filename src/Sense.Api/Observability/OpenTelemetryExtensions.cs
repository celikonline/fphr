using System.Reflection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.Observability;

namespace Sense.Api.Observability;

/// <summary>
/// OpenTelemetry dağıtık izleme (tracing) ve metrik kurulumu.
/// <list type="bullet">
/// <item>İzler: OTLP/gRPC ile Jaeger'in yerel OTLP alıcısına (varsayılan http://localhost:4317) gider.</item>
/// <item>Metrikler: Prometheus'un scrape ettiği <c>/metrics</c> uç noktasından sunulur (pull modeli; Jaeger metrik kabul etmez).</item>
/// </list>
/// Loglar ayrı olarak Serilog üzerinden Loki'ye akar (bkz. <see cref="LogExportExtensions"/>).
/// Altyapı: infra/docker-compose.yml, profile "obs" (jaeger, prometheus, loki, grafana).
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>Npgsql'in kendi Meter'ı (bağlantı havuzu ve komut metrikleri).</summary>
    private const string NpgsqlMeterName = "Npgsql";

    /// <summary>.NET 9+ SignalR hub çağrıları için yerleşik ActivitySource.</summary>
    private const string SignalRServerSourceName = "Microsoft.AspNetCore.SignalR.Server";

    /// <summary>.NET 10'un kimlik doğrulama/yetkilendirme meter'ları (AddAspNetCoreInstrumentation bunları eklemez).</summary>
    private static readonly string[] AspNetCoreSecurityMeters =
    [
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
    ];

    internal static readonly string ServiceVersion =
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
                // Üst servis örnekleme kararı verdiyse ona uyulur; kök izlerde oran uygulanır.
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TracingSampleRatio)))
                .AddAspNetCoreInstrumentation(o =>
                {
                    // Health check probe ve Prometheus scrape trafiği trace gürültüsü yaratmasın.
                    o.Filter = httpContext => !IsInfrastructureRequest(httpContext.Request.Path, options);
                    o.RecordException = true;
                })
                .AddHttpClientInstrumentation(o => o.RecordException = true)
                .AddNpgsql()
                .AddSource(DiagnosticsDefaults.ActivitySourceName)
                .AddSource(SenseActivitySources.DocumentsName)
                .AddSource(SenseActivitySources.BackgroundJobsName)
                .AddSource(SignalRServerSourceName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)));
        }

        if (options.EnableMetrics)
        {
            otel.WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(NpgsqlMeterName)
                .AddMeter(AspNetCoreSecurityMeters)
                .AddMeter(SenseMeters.Name)
                .AddPrometheusExporter());
        }

        return services;
    }

    /// <summary>Metrikler etkinse Prometheus scrape uç noktasını (varsayılan <c>/metrics</c>) eşler.</summary>
    public static IEndpointRouteBuilder MapSenseMetrics(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
    {
        var options = configuration.GetSection(ConfigurationSections.Observability).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        if (options.EnableMetrics)
        {
            endpoints.MapPrometheusScrapingEndpoint(options.MetricsPath);
        }

        return endpoints;
    }

    /// <summary>Health probe ve metrik scrape istekleri: iz ve istek logu gürültüsünden hariç tutulur.</summary>
    public static bool IsInfrastructureRequest(PathString path, ObservabilityOptions options) =>
        path.StartsWithSegments(HostConstants.HealthPath) || path.StartsWithSegments(options.MetricsPath);
}
