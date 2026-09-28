using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sense.Modules.Attendance.Api.Controllers;
using Sense.Modules.Attendance.Api.TerminalCredentials;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Localization;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// PIN varlık ipucu hafifletmesi (T2 tur 3): kimlik ekleme/etkinleştirme uçlarında kullanıcı başına istek sınırı (30/dk) ve "PIN başkasında
/// aktif" ret bütçesi (5/5 dk). Aşılınca 429 genel ileti + Retry-After; işleyici çağrılmaz; diğer kullanıcılar etkilenmez.
/// </summary>
public sealed class TerminalCredentialThrottleTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly TerminalCredentialThrottle _throttle;

    public TerminalCredentialThrottleTests()
    {
        _throttle = new TerminalCredentialThrottle(Options.Create(new TerminalCredentialThrottleOptions()), _clock);
        _dispatcher.Send(Arg.Any<AddEmployeeTerminalCredentialCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.NewGuid()));
        _dispatcher.Send(Arg.Any<ActivateEmployeeTerminalCredentialCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task Credential_post_rate_limited_per_user()
    {
        var user = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 30; i++)
        {
            Status(await Controller(user).Add(Card(), ct)).ShouldBe(StatusCodes.Status200OK);
        }

        var limited = Controller(user);
        var rejected = await limited.Add(Card(), ct);
        Status(rejected).ShouldBe(StatusCodes.Status429TooManyRequests);
        ((ProblemDetails)((ObjectResult)rejected).Value!).Extensions[ProblemDetailsDefaults.CodeExtension].ShouldBe(ErrorCodes.RateLimitExceeded);
        limited.Response.Headers.RetryAfter.ToString().ShouldNotBeEmpty();
        Status(await Controller(user).Activate(Guid.NewGuid(), ct)).ShouldBe(StatusCodes.Status429TooManyRequests);
        await _dispatcher.Received(30).Send(Arg.Any<AddEmployeeTerminalCredentialCommand>(), Arg.Any<CancellationToken>());

        Status(await Controller(Guid.NewGuid()).Add(Card(), ct)).ShouldBe(StatusCodes.Status200OK);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Status(await Controller(user).Add(Card(), ct)).ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Pin_in_use_rejects_over_budget_are_throttled()
    {
        var user = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        _dispatcher.Send(Arg.Is<AddEmployeeTerminalCredentialCommand>(c => c.Kind == TerminalCredentialKind.Pin), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(Error.Rule(TerminalCredentialErrors.Rejected)));

        for (var i = 0; i < 5; i++)
        {
            Status(await Controller(user).Add(Pin(), ct)).ShouldBe(StatusCodes.Status422UnprocessableEntity);
        }

        var blocked = await Controller(user).Add(Pin(), ct);
        Status(blocked).ShouldBe(StatusCodes.Status429TooManyRequests);
        ((ProblemDetails)((ObjectResult)blocked).Value!).Extensions[ProblemDetailsDefaults.CodeExtension].ShouldBe(ErrorCodes.RateLimitExceeded);
        Status(await Controller(user).Activate(Guid.NewGuid(), ct)).ShouldBe(StatusCodes.Status429TooManyRequests);
        await _dispatcher.Received(5).Send(Arg.Is<AddEmployeeTerminalCredentialCommand>(c => c.Kind == TerminalCredentialKind.Pin), Arg.Any<CancellationToken>());

        // Kart ekleme ve başka kullanıcı etkilenmez; bütçe 5 dk sonra yenilenir.
        Status(await Controller(user).Add(Card(), ct)).ShouldBe(StatusCodes.Status200OK);
        Status(await Controller(Guid.NewGuid()).Add(Pin(), ct)).ShouldBe(StatusCodes.Status422UnprocessableEntity);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Status(await Controller(user).Add(Pin(), ct)).ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Other_failures_do_not_consume_the_pin_budget()
    {
        var user = Guid.NewGuid();
        _dispatcher.Send(Arg.Is<AddEmployeeTerminalCredentialCommand>(c => c.Kind == TerminalCredentialKind.Pin), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(Error.Validation(TerminalCredentialErrors.PinInvalid)));

        for (var i = 0; i < 10; i++)
        {
            Status(await Controller(user).Add(Pin(), TestContext.Current.CancellationToken)).ShouldBe(StatusCodes.Status400BadRequest);
        }

        _throttle.IsPinRejectBudgetExhausted(user, out _).ShouldBeFalse();
    }

    private static AddTerminalCredentialRequest Card() => new(Guid.NewGuid(), TerminalCredentialKind.Card, "C-1");

    private static AddTerminalCredentialRequest Pin() => new(Guid.NewGuid(), TerminalCredentialKind.Pin, "471193");

    private static int? Status(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult s => s.StatusCode,
        _ => null,
    };

    private TerminalCredentialsController Controller(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        var services = new ServiceCollection()
            .AddSingleton(_dispatcher)
            .AddSingleton(_throttle)
            .AddSingleton(user)
            .AddSingleton(Substitute.For<IErrorLocalizer>())
            .AddSingleton(Options.Create(new ProblemMappingOptions()))
            .BuildServiceProvider();
        return new TerminalCredentialsController { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } } };
    }
}
