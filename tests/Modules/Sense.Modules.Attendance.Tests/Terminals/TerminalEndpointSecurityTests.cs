using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly TerminalIdentity _terminal = new(Guid.NewGuid(), Guid.NewGuid(), TerminalDirectionMode.InOnly, Guid.NewGuid());
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
        typeof(TerminalIngestController).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(TerminalKeyDefaults.RateLimitPolicy);
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
    public async Task Tenant_scope_is_opened_from_the_authenticated_terminal()
    {
        var services = new ServiceCollection().AddSingleton(Substitute.For<Sense.Shared.Contracts.Context.ITenantContextSetter>()).BuildServiceProvider();
        var setter = services.GetRequiredService<Sense.Shared.Contracts.Context.ITenantContextSetter>();
        var context = NewContext(services);
        context.User = TerminalPrincipal.Create(_terminal, TerminalKeyDefaults.AuthenticationScheme);
        var executing = new Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext(
            new Microsoft.AspNetCore.Mvc.ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), [], []);
        var ran = false;

        await new TerminalTenantScopeAttribute().OnResourceExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult<Microsoft.AspNetCore.Mvc.Filters.ResourceExecutedContext>(null!);
        });

        ran.ShouldBeTrue();
        setter.Received(1).BeginScope(_terminal.TenantId, Arg.Any<string?>());
        new TerminalTenantScopeAttribute().Order.ShouldBe(int.MinValue);
    }

    [Fact]
    public async Task Rate_limit_returns_429()
    {
        var perTerminal = BuildRateLimitedPipeline(permitLimit: 2, ipPermitLimit: 100);
        var keyA = TerminalApiKey.Generate().Key;

        var sameKey = new[] { await perTerminal(keyA, "10.0.0.1"), await perTerminal(keyA, "10.0.0.1"), await perTerminal(keyA, "10.0.0.1") };
        var otherTerminalSameIp = await perTerminal(TerminalApiKey.Generate().Key, "10.0.0.1");

        sameKey.ShouldBe([200, 200, StatusCodes.Status429TooManyRequests]);
        otherTerminalSameIp.ShouldBe(200);
        new TerminalIngestRateLimitOptions().PermitLimit.ShouldBe(60);
    }

    [Fact]
    public async Task Rate_limit_per_ip_caps_requests_with_ever_changing_keys()
    {
        var perIp = BuildRateLimitedPipeline(permitLimit: 100, ipPermitLimit: 3);
        var statuses = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add(await perIp(TerminalApiKey.Generate().Key, "10.0.0.1"));
        }

        var otherIp = await perIp(TerminalApiKey.Generate().Key, "10.0.0.2");

        statuses.ShouldBe([200, 200, 200, StatusCodes.Status429TooManyRequests]);
        otherIp.ShouldBe(200);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_authenticator);
        services.AddAuthentication(FakeJwtScheme).AddScheme<AuthenticationSchemeOptions, FakeJwtHandler>(FakeJwtScheme, _ => { });
        services.AddTerminalIngestEndpointSecurity(new ConfigurationBuilder().Build());
        services.AddAuthorization();
        return services.BuildServiceProvider();
    }

    /// <summary>Her istek kendi DI kapsamında (ASP.NET gibi): kimlik doğrulama işleyicileri kapsam başına önbelleğe alınır.</summary>
    private static DefaultHttpContext NewContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services.CreateScope().ServiceProvider };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<AuthorizationPolicy> IngestPolicyAsync(IServiceProvider services)
    {
        var authorizeData = typeof(TerminalIngestController).GetCustomAttributes<AuthorizeAttribute>(true).Cast<IAuthorizeData>().ToList();
        authorizeData.ShouldNotBeEmpty();
        return (await AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(), authorizeData))!;
    }

    private static Func<string, string, Task<int>> BuildRateLimitedPipeline(int permitLimit, int ipPermitLimit)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [TerminalIngestRateLimitOptions.Section + ":PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [TerminalIngestRateLimitOptions.Section + ":IpPermitLimit"] = ipPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(o => o.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        services.AddTerminalIngestRateLimiting(configuration);
        var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseRateLimiter();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var pipeline = app.Build();
        var endpoint = new Endpoint(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute(TerminalKeyDefaults.RateLimitPolicy)), "terminal-ingest");

        return async (key, ip) =>
        {
            var context = NewContext(provider);
            context.Request.Headers[TerminalKeyDefaults.HeaderName] = key;
            context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            context.SetEndpoint(endpoint);
            await pipeline(context);
            return context.Response.StatusCode;
        };
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
