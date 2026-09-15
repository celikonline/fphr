using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Observability;

namespace Sense.Api.Middleware;

/// <summary>
/// Pipeline'ın en başında çalışır (kimlik doğrulamadan bile önce): X-Correlation-Id isteği okur, yoksa üretir;
/// HttpContext.Items ve AsyncLocal tabanlı <see cref="ICorrelationIdContextSetter"/> içine yazar ve yanıt
/// başlığına ekler. Böylece hem sonraki middleware'ler (RequestContextMiddleware, GlobalExceptionHandler)
/// hem de Serilog <see cref="CorrelationIdEnricher"/> aynı kimliği görür.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationIdContextSetter correlationIdContextSetter)
    {
        var correlationId = context.Request.Headers[SenseHeaderNames.CorrelationId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items[CorrelationIdContext.HttpContextItemKey] = correlationId;
        context.Response.Headers[SenseHeaderNames.CorrelationId] = correlationId;

        using (correlationIdContextSetter.BeginScope(correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>Pipeline'a olabildiğince erken eklenmelidir (UseExceptionHandler'dan önce).</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
