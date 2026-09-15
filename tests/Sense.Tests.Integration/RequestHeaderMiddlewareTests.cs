using Microsoft.AspNetCore.Http;
using Sense.Api.Middleware;
using Sense.Shared.Contracts.Security;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>ClientContextMiddleware ve SecurityHeadersMiddleware: tam host açmadan doğrudan middleware çağrısıyla.</summary>
public sealed class RequestHeaderMiddlewareTests
{
    [Fact]
    public async Task ClientContext_PushesClientHeaders_IntoLogContext()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var middleware = new ClientContextMiddleware(_ =>
        {
            logger.Information("probe");
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[SenseHeaderNames.ClientApp] = "mobile";
        httpContext.Request.Headers[SenseHeaderNames.ClientVersion] = "1.4.2";
        httpContext.Request.Headers[SenseHeaderNames.ClientPlatform] = "ios";

        await middleware.InvokeAsync(httpContext);

        var evt = sink.Events.ShouldHaveSingleItem();
        evt.Properties[ClientContextMiddleware.ClientAppProperty].ToString().ShouldContain("mobile");
        evt.Properties[ClientContextMiddleware.ClientVersionProperty].ToString().ShouldContain("1.4.2");
        evt.Properties[ClientContextMiddleware.ClientPlatformProperty].ToString().ShouldContain("ios");
    }

    [Fact]
    public async Task ClientContext_IgnoresUnsafeOrOverlongValues()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var middleware = new ClientContextMiddleware(_ =>
        {
            logger.Information("probe");
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[SenseHeaderNames.ClientApp] = "web app\" injected";
        httpContext.Request.Headers[SenseHeaderNames.ClientVersion] = new string('1', HeaderValues.MaxClientInfoLength + 1);

        await middleware.InvokeAsync(httpContext);

        var evt = sink.Events.ShouldHaveSingleItem();
        evt.Properties.ShouldNotContainKey(ClientContextMiddleware.ClientAppProperty);
        evt.Properties.ShouldNotContainKey(ClientContextMiddleware.ClientVersionProperty);
    }

    [Fact]
    public async Task SecurityHeaders_AddsDefaults_WithoutOverwritingExplicitValues()
    {
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Headers["Referrer-Policy"] = "strict-origin";

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.Headers["X-Content-Type-Options"].ToString().ShouldBe("nosniff");
        httpContext.Response.Headers["X-Frame-Options"].ToString().ShouldBe("DENY");
        httpContext.Response.Headers["Referrer-Policy"].ToString().ShouldBe("strict-origin");
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
