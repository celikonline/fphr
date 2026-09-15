using Microsoft.AspNetCore.Http;
using Sense.Api.Middleware;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Observability;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// CorrelationIdMiddleware: istek → yanıt başlığı → AsyncLocal bağlamı akışını, tam bir host açmadan
/// (WebApplicationFactory olmadan) doğrudan middleware çağırarak doğrular.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_GeneratesCorrelationId_WhenHeaderAbsent()
    {
        var contextSetter = new CorrelationIdContext();
        string? seenDuringRequest = null;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            seenDuringRequest = contextSetter.CorrelationId;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(httpContext, contextSetter);

        seenDuringRequest.ShouldNotBeNullOrEmpty();
        httpContext.Response.Headers[SenseHeaderNames.CorrelationId].ToString().ShouldBe(seenDuringRequest);
        httpContext.Items[CorrelationIdContext.HttpContextItemKey].ShouldBe(seenDuringRequest);

        // Middleware kapsamı sona erdiğinde AsyncLocal önceki (boş) duruma dönmelidir.
        contextSetter.CorrelationId.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeAsync_PreservesIncomingCorrelationId_WhenHeaderPresent()
    {
        var contextSetter = new CorrelationIdContext();
        const string Incoming = "client-supplied-correlation-id";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[SenseHeaderNames.CorrelationId] = Incoming;

        await middleware.InvokeAsync(httpContext, contextSetter);

        httpContext.Response.Headers[SenseHeaderNames.CorrelationId].ToString().ShouldBe(Incoming);
        httpContext.Items[CorrelationIdContext.HttpContextItemKey].ShouldBe(Incoming);
    }
}
