using System.Diagnostics;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Observability;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace Sense.Api.Middleware;

/// <summary>
/// İstemci kimlik başlıklarını (<c>X-Client-App</c>, <c>X-Client-Version</c>, <c>X-Client-Platform</c>) okur; isteğin tüm
/// loglarına (Serilog LogContext) ve aktif span'e ekler. Böylece "hangi uygulama/sürüm hata üretiyor" sorusu Loki ve
/// Jaeger'de filtrelenebilir. Başlıklar opsiyoneldir ve yalnızca gözlemlenebilirlik içindir — asla yetkilendirme
/// kararında kullanılmaz. Metrik etiketi yapılmaz (sürüm değerleri kardinaliteyi şişirir).
/// </summary>
public sealed class ClientContextMiddleware(RequestDelegate next)
{
    public const string ClientAppProperty = "ClientApp";
    public const string ClientVersionProperty = "ClientVersion";
    public const string ClientPlatformProperty = "ClientPlatform";

    private static readonly (string Header, string LogProperty, string SpanTag)[] Fields =
    [
        (SenseHeaderNames.ClientApp, ClientAppProperty, SenseActivitySources.Tags.ClientApp),
        (SenseHeaderNames.ClientVersion, ClientVersionProperty, SenseActivitySources.Tags.ClientVersion),
        (SenseHeaderNames.ClientPlatform, ClientPlatformProperty, SenseActivitySources.Tags.ClientPlatform),
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        List<ILogEventEnricher>? enrichers = null;
        foreach (var (header, logProperty, spanTag) in Fields)
        {
            var value = HeaderValues.Read(context.Request.Headers, header, HeaderValues.MaxClientInfoLength);
            if (value is null)
            {
                continue;
            }

            Activity.Current?.SetTag(spanTag, value);
            (enrichers ??= []).Add(new PropertyEnricher(logProperty, value));
        }

        if (enrichers is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        using (LogContext.Push([.. enrichers]))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}

public static class ClientContextMiddlewareExtensions
{
    /// <summary>UseCorrelationId'den hemen sonra, request logging'den önce eklenmelidir (tamamlanma logu da alanları taşısın).</summary>
    public static IApplicationBuilder UseClientContext(this IApplicationBuilder app) =>
        app.UseMiddleware<ClientContextMiddleware>();
}
