using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Observability;

namespace Sense.Api.Observability;

/// <summary>
/// Sentry hata izleme entegrasyonu. DSN yoksa Sentry hiç başlatılmaz (local dev'de varsayılan budur:
/// SENTRY_DSN ortam değişkeni veya "Observability:SentryDsn" appsettings anahtarı ayarlanmadıkça sessizce
/// devre dışı kalır — geliştirme hatalarının prod Sentry projesine sızmasını önler).
/// </summary>
public static class SentryExtensions
{
    public static WebApplicationBuilder AddSenseSentry(this WebApplicationBuilder builder)
    {
        var dsn = ResolveDsn(builder.Configuration);
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return builder;
        }

        var observability = builder.Configuration.GetSection(ConfigurationSections.Observability).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        builder.WebHost.UseSentry(o =>
        {
            o.Dsn = dsn;
            o.Environment = builder.Environment.EnvironmentName;
            o.Debug = builder.Environment.IsDevelopment();

            // Hatalar her zaman %100 yakalanır; performans (trace) örneklemesi ayrı ve daha düşüktür.
            o.TracesSampleRate = observability.SentryTracesSampleRate;

            // PII'yi biz kontrol ediyoruz (TenantId/UserId/CorrelationId dışında hiçbir şey eklenmez).
            o.SendDefaultPii = false;

            o.SetBeforeSend((sentryEvent, _) => Enrich(sentryEvent));
        });

        return builder;
    }

    /// <summary>SENTRY_DSN ortam değişkeni, appsettings'teki Observability:SentryDsn'e her zaman önceliklidir.</summary>
    private static string? ResolveDsn(IConfiguration configuration)
    {
        var envDsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
        if (!string.IsNullOrWhiteSpace(envDsn))
        {
            return envDsn;
        }

        var configured = configuration[$"{ConfigurationSections.Observability}:{nameof(ObservabilityOptions.SentryDsn)}"];
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    /// <summary>Olayı kiracı/correlation bağlamıyla etiketler; belge/kayıt içeriği asla eklenmez.</summary>
    private static Sentry.SentryEvent Enrich(Sentry.SentryEvent sentryEvent)
    {
        if (TenantContext.CurrentTenantIdOrNull is { } tenantId)
        {
            sentryEvent.SetTag("tenant_id", tenantId.ToString());
        }

        var correlationId = new CorrelationIdContext().CorrelationId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            sentryEvent.SetTag("correlation_id", correlationId);
        }

        return sentryEvent;
    }
}
