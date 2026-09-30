using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Attendance.Api.Controllers;
using Sense.Modules.Attendance.Api.Terminals;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// Okutma ucunun HTTP güvenliği (Z5.1): X-Terminal-Key şeması (401 sızıntısız), JWT ile erişilemez, kiracı yalnız terminalden,
/// hız sınırı (429) - gerçek ASP.NET kimlik doğrulama/yetkilendirme/hız sınırı bileşenleriyle, üretimdeki kayıt yöntemleri kullanılarak.
/// </summary>
public sealed class TerminalEndpointSecurityTests
{
    private const string FakeJwtScheme = "Bearer";
    private readonly ITerminalKeyAuthenticator _authenticator = Substitute.For<ITerminalKeyAuthenticator>();
    private readonly TerminalIdentity _terminal = new(Guid.NewGuid(), Guid.NewGuid(), TerminalDirectionMode.InOnly, Guid.NewGuid(), "GATE-1");
    private readonly string _validKey = TerminalApiKey.Generate().Key;

    public TerminalEndpointSecurityTests()
    {
        _authenticator.AuthenticateAsync(default, default).ReturnsForAnyArgs((TerminalIdentity?)null);
        _authenticator.AuthenticateAsync(_validKey, Arg.Any<CancellationToken>()).Returns(_terminal);
    }

    [Fact]
    public async Task Invalid_key_returns_401_without_tenant_info()
    {
        var context = NewContext(BuildServices());
        context.Request.Headers[TerminalKeyDefaults.HeaderName] = TerminalApiKey.Generate().Key;

        var result = await context.AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme);
        await context.ChallengeAsync(TerminalKeyDefaults.AuthenticationScheme);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Message.ShouldBe(TerminalKeyDefaults.InvalidKeyMessage);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        context.Response.Body.Length.ShouldBe(0);
        context.Response.Headers.Keys.ShouldBe([Microsoft.Net.Http.Headers.HeaderNames.WWWAuthenticate]);
        context.Response.Headers.WWWAuthenticate.ToString().ShouldBe(TerminalKeyDefaults.AuthenticationScheme);
        var everything = string.Join('|', context.Response.Headers.Select(h => h.Value.ToString())) + result.Failure.Message;
        everything.ShouldNotContain(_terminal.TenantId.ToString());
        everything.ShouldNotContain(_terminal.TerminalId.ToString());
    }

    [Fact]
    public async Task Valid_key_authenticates_the_terminal_and_its_tenant()
    {
        var context = NewContext(BuildServices());
        context.Request.Headers[TerminalKeyDefaults.HeaderName] = _validKey;

        var result = await context.AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme);

        result.Succeeded.ShouldBeTrue();
        TerminalPrincipal.Read(result.Principal!).ShouldBe(_terminal);
    }

    [Fact]
    public async Task Missing_header_is_no_result_and_mixed_credentials_are_rejected()
    {
        var services = BuildServices();
        var missing = await NewContext(services).AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme);

        var mixed = NewContext(services);
        mixed.Request.Headers[TerminalKeyDefaults.HeaderName] = _validKey;
        mixed.Request.Headers.Authorization = "Bearer user-token";
        var mixedResult = await mixed.AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme);

        missing.None.ShouldBeTrue();
        mixedResult.Succeeded.ShouldBeFalse();
        await _authenticator.DidNotReceiveWithAnyArgs().AuthenticateAsync(default, default);
    }

    [Fact]
    public async Task Ingest_endpoint_with_a_valid_jwt_only_returns_401()
    {
        var services = BuildServices();
        var policy = await IngestPolicyAsync(services);
        var context = NewContext(services);
        context.Request.Headers.Authorization = "Bearer valid-user-token";

        var authenticated = await services.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, context);
        var authorized = await services.GetRequiredService<IPolicyEvaluator>().AuthorizeAsync(policy, authenticated, context, resource: null);
        await context.ChallengeAsync(TerminalKeyDefaults.AuthenticationScheme);

        (await context.AuthenticateAsync(FakeJwtScheme)).Succeeded.ShouldBeTrue("the fake JWT scheme itself accepts the token");
        policy.AuthenticationSchemes.ShouldBe([TerminalKeyDefaults.AuthenticationScheme]);
        authenticated.Succeeded.ShouldBeFalse();
        authorized.Challenged.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Ingest_endpoint_accepts_only_the_terminal_key_scheme()
    {
        var services = BuildServices();
        var policy = await IngestPolicyAsync(services);
        var context = NewContext(services);
        context.Request.Headers[TerminalKeyDefaults.HeaderName] = _validKey;

        var authenticated = await services.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, context);
        var authorized = await services.GetRequiredService<IPolicyEvaluator>().AuthorizeAsync(policy, authenticated, context, resource: null);

        authorized.Succeeded.ShouldBeTrue();
        typeof(TerminalIngestController).GetCustomAttributes<AllowAnonymousAttribute>(true).ShouldBeEmpty();
        typeof(TerminalIngestController).GetMethods().SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>(true)).ShouldBeEmpty();
        typeof(TerminalIngestController).GetCustomAttribute<TerminalTenantScopeAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void Terminal_identity_is_read_only_from_the_terminal_scheme()
    {
        var forged = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TerminalKeyDefaults.TerminalIdClaim, Guid.NewGuid().ToString()), new Claim(TerminalKeyDefaults.TenantIdClaim, Guid.NewGuid().ToString()),
             new Claim(TerminalKeyDefaults.DirectionModeClaim, "0")],
            FakeJwtScheme));

        TerminalPrincipal.Read(forged).ShouldBeNull();
        TerminalPrincipal.Read(TerminalPrincipal.Create(_terminal, TerminalKeyDefaults.AuthenticationScheme)).ShouldBe(_terminal);
    }

    [Fact]
    public async Task Tenant_scope_and_audit_actor_come_from_the_authenticated_terminal()
    {
        var services = new ServiceCollection()
            .AddSingleton(Substitute.For<Sense.Shared.Contracts.Context.ITenantContextSetter>())
            .AddSingleton(Substitute.For<Sense.Shared.Contracts.Context.IAuditSourceAccessor>())
            .BuildServiceProvider();
        var setter = services.GetRequiredService<Sense.Shared.Contracts.Context.ITenantContextSetter>();
        var context = NewContext(services);
        context.User = TerminalPrincipal.Create(_terminal, TerminalKeyDefaults.AuthenticationScheme);
        var executing = new Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext(
            new Microsoft.AspNetCore.Mvc.ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), [], []);
        var ran = false;

        await new TerminalTenantScopeAttribute().OnResourceExecutionAsync(executing, () =>
        {
            var user = new Sense.Shared.Infrastructure.Context.CurrentUserAccessor();
            ran = true;
            (user.DisplayName, user.IsAuthenticated, user.UserId, user.IsPlatformAdmin, user.Roles.Count).ShouldBe(("terminal:GATE-1", false, (Guid?)null, false, 0));
            return Task.FromResult<Microsoft.AspNetCore.Mvc.Filters.ResourceExecutedContext>(null!);
        });

        ran.ShouldBeTrue();
        new Sense.Shared.Infrastructure.Context.CurrentUserAccessor().DisplayName.ShouldBeNull();
        setter.Received(1).BeginScope(_terminal.TenantId, Arg.Any<string?>());
        services.GetRequiredService<Sense.Shared.Contracts.Context.IAuditSourceAccessor>().Received(1).BeginScope(Sense.Shared.Contracts.Context.AuditSources.Api);
        new TerminalTenantScopeAttribute().Order.ShouldBe(int.MinValue);
    }

    [Fact]
    public void Rate_limit_returns_429()
    {
        using var throttle = new TerminalRequestThrottle(Options.Create(new TerminalIngestRateLimitOptions { PermitLimit = 2 }));
        var terminalA = Guid.NewGuid();

        var sameTerminal = new[] { throttle.TryAcquire(terminalA, 1, out _), throttle.TryAcquire(terminalA, 1, out _), throttle.TryAcquire(terminalA, 1, out var retryAfter) };
        var otherTerminal = throttle.TryAcquire(Guid.NewGuid(), 1, out _);

        sameTerminal.ShouldBe([true, true, false]);
        retryAfter.ShouldNotBeNull().ShouldBeGreaterThan(TimeSpan.Zero);
        otherTerminal.ShouldBeTrue();
        new TerminalIngestRateLimitOptions().PermitLimit.ShouldBe(60);
    }

    [Fact]
    public void Rate_cost_scales_with_item_count()
    {
        using var throttle = new TerminalRequestThrottle(Options.Create(new TerminalIngestRateLimitOptions { PermitLimit = 60 }));
        var terminal = Guid.NewGuid();

        var costs = new[] { 0, 1, 50, 51, 500 }.Select(TerminalRequestThrottle.CostOf).ToList();
        var fullBatches = Enumerable.Range(0, 7).Select(i => throttle.TryAcquire(terminal, TerminalRequestThrottle.CostOf(500), out _)).ToList();

        costs.ShouldBe([1, 1, 1, 2, 10]);
        fullBatches.ShouldBe([true, true, true, true, true, true, false]);
    }

    [Fact]
    public async Task Valid_key_never_consumes_ip_bucket()
    {
        var services = BuildServices(failedAuthPermitLimit: 2);

        var valid = new List<bool>();
        for (var i = 0; i < 10; i++)
        {
            valid.Add((await AuthenticateFromAsync(services, _validKey, "10.0.0.1")).Succeeded);
        }

        var failures = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            failures.Add(await ChallengeStatusAsync(services, TerminalApiKey.Generate().Key, "10.0.0.1"));
        }

        valid.ShouldAllBe(ok => ok);
        failures.ShouldBe([401, 401, 429]);
    }

    [Fact]
    public async Task Invalid_key_burst_gets_429_but_valid_terminal_unaffected()
    {
        var services = BuildServices(failedAuthPermitLimit: 3);
        var statuses = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            statuses.Add(await ChallengeStatusAsync(services, TerminalApiKey.Generate().Key, "10.0.0.9"));
        }

        var blocked = NewContext(services, "10.0.0.9");
        blocked.Request.Headers[TerminalKeyDefaults.HeaderName] = "not-even-a-key";
        await blocked.AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme);
        await blocked.ChallengeAsync(TerminalKeyDefaults.AuthenticationScheme);
        var validSameIp = await AuthenticateFromAsync(services, _validKey, "10.0.0.9");
        var otherIp = await ChallengeStatusAsync(services, TerminalApiKey.Generate().Key, "10.0.0.10");

        statuses.ShouldBe([401, 401, 401, 429, 429, 429]);
        int.Parse(blocked.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
        blocked.Response.Body.Length.ShouldBe(0);
        validSameIp.Succeeded.ShouldBeTrue();
        otherIp.ShouldBe(401);
    }

    [Fact]
    public void Ip_bucket_count_is_bounded()
    {
        var tracker = new TerminalAuthFailureTracker(
            Options.Create(new TerminalIngestRateLimitOptions { FailedAuthPermitLimit = 1, MaxTrackedIps = 3 }), TimeProvider.System);

        for (var i = 0; i < 50; i++)
        {
            tracker.RegisterFailure("10.1.0." + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        tracker.TrackedCount.ShouldBe(3);
        tracker.RegisterFailure("10.1.0.49").ShouldNotBeNull("the most recent IP is still tracked and over its limit");
    }

    private async Task<AuthenticateResult> AuthenticateFromAsync(ServiceProvider services, string key, string ip)
    {
        var context = NewContext(services, ip);
        context.Request.Headers[TerminalKeyDefaults.HeaderName] = key;
        return await context.AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme);
    }

    private static async Task<int> ChallengeStatusAsync(ServiceProvider services, string key, string ip)
    {
        var context = NewContext(services, ip);
        context.Request.Headers[TerminalKeyDefaults.HeaderName] = key;
        (await context.AuthenticateAsync(TerminalKeyDefaults.AuthenticationScheme)).Succeeded.ShouldBeFalse();
        await context.ChallengeAsync(TerminalKeyDefaults.AuthenticationScheme);
        return context.Response.StatusCode;
    }

    private ServiceProvider BuildServices(int? failedAuthPermitLimit = null)
    {
        var settings = new Dictionary<string, string?>();
        if (failedAuthPermitLimit is { } limit)
        {
            settings[TerminalIngestRateLimitOptions.Section + ":" + nameof(TerminalIngestRateLimitOptions.FailedAuthPermitLimit)] =
                limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_authenticator);
        services.AddAuthentication(FakeJwtScheme).AddScheme<AuthenticationSchemeOptions, FakeJwtHandler>(FakeJwtScheme, _ => { });
        services.AddTerminalIngestEndpointSecurity(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddAuthorization();
        return services.BuildServiceProvider();
    }

    /// <summary>Her istek kendi DI kapsamında (ASP.NET gibi): kimlik doğrulama işleyicileri kapsam başına önbelleğe alınır.</summary>
    private static DefaultHttpContext NewContext(IServiceProvider services, string? ip = null)
    {
        var context = new DefaultHttpContext { RequestServices = services.CreateScope().ServiceProvider };
        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<AuthorizationPolicy> IngestPolicyAsync(IServiceProvider services)
    {
        var authorizeData = typeof(TerminalIngestController).GetCustomAttributes<AuthorizeAttribute>(true).Cast<IAuthorizeData>().ToList();
        authorizeData.ShouldNotBeEmpty();
        return (await AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(), authorizeData))!;
    }

    /// <summary>Geçerli bir kullanıcı JWT'sini temsil eder: Authorization: Bearer varsa her zaman doğrular.</summary>
    private sealed class FakeJwtHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal)
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("tid", Guid.NewGuid().ToString())], FakeJwtScheme)),
                    FakeJwtScheme))
                : AuthenticateResult.NoResult());
    }
}
