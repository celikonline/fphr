using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure;
using Sense.Modules.Attendance.Infrastructure.Terminals;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>
/// Pepper isteğe bağlıdır (lazy doğrulama): yoksa API açılır ve yalnız uyarı loglanır; anahtar üretimi/doğrulaması, terminal yönetimi ve
/// nabız pepper'sız çalışır (yalnız kimlik işlemleri 503 - bkz. <see cref="TerminalCredentialHasherTests"/>, <see cref="TerminalPunchIngestTests"/>).
/// </summary>
public sealed class TerminalPepperOptionalTests
{
    private const string ShortPepperValue = "short-secret-value";

    [Theory]
    [InlineData(null)]
    [InlineData(ShortPepperValue)]
    public async Task Api_starts_without_pepper_and_logs_warning(string? pepper)
    {
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(pepper, logs);
        using var provider = services.BuildServiceProvider();

        Should.NotThrow(() => provider.GetService<IStartupValidator>()?.Validate());
        Should.NotThrow(() => provider.GetRequiredService<IOptions<TerminalSecurityOptions>>().Value);
        provider.GetRequiredService<ITerminalCredentialHasher>().IsAvailable.ShouldBeFalse();

        var startupCheck = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(TerminalSecurityStartupCheck));
        await ((IHostedService)ActivatorUtilities.CreateInstance(provider, startupCheck.ImplementationType!)).StartAsync(TestContext.Current.CancellationToken);

        var warning = logs.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("Attendance:Terminals:CredentialPepper");
        warning.Message.ShouldNotContain(ShortPepperValue);
    }

    [Fact]
    public async Task Configured_pepper_logs_nothing()
    {
        var logs = new CapturingLoggerProvider();
        var services = BuildServices(TerminalTestData.TestPepper, logs);
        using var provider = services.BuildServiceProvider();

        await ActivatorUtilities.CreateInstance<TerminalSecurityStartupCheck>(provider).StartAsync(TestContext.Current.CancellationToken);

        logs.Entries.ShouldBeEmpty();
        provider.GetRequiredService<ITerminalCredentialHasher>().IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task Terminal_management_works_without_pepper()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        var terminals = Substitute.For<ITerminalRepository>();
        Terminal? stored = null;
        terminals.AddTerminal(Arg.Do<Terminal>(t => stored = t));
        terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync(default!, default).ReturnsForAnyArgs(_ => stored is null ? [] : [stored]);
        terminals.GetTerminalByIdAsync(default, default).ReturnsForAnyArgs(_ => Sense.Shared.Kernel.Results.Result.Success(stored!));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var services = BuildServices(pepper: null, new CapturingLoggerProvider());
        services.Replace(ServiceDescriptor.Scoped(_ => terminals));
        services.AddSingleton(tenant);
        services.AddSingleton(Substitute.For<ILocationLookup>());
        services.AddSingleton(Substitute.For<IPunchRecorder>());
        services.AddSingleton(TerminalTestData.Contacts());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var created = await ActivatorUtilities.CreateInstance<CreateTerminalHandler>(scope.ServiceProvider).Handle(
            new CreateTerminalCommand(new TerminalInput("T-01", "Ana giriş", null, null, TerminalDirectionMode.InOnly)), ct);
        var identity = await scope.ServiceProvider.GetRequiredService<ITerminalKeyAuthenticator>().AuthenticateAsync(created.Value.ApiKey, ct);
        var heartbeat = await scope.ServiceProvider.GetRequiredService<TerminalPunchIngest>().HeartbeatAsync(identity!, ct);
        var rotated = await ActivatorUtilities.CreateInstance<RotateTerminalApiKeyHandler>(scope.ServiceProvider).Handle(new RotateTerminalApiKeyCommand(stored!.Id), ct);

        created.IsSuccess.ShouldBeTrue();
        identity.ShouldNotBeNull().TenantId.ShouldBe(tenantId);
        heartbeat.IsSuccess.ShouldBeTrue();
        rotated.IsSuccess.ShouldBeTrue();
    }

    private static ServiceCollection BuildServices(string? pepper, CapturingLoggerProvider logs)
    {
        var settings = new Dictionary<string, string?>();
        if (pepper is not null)
        {
            settings[TerminalSecurityOptions.Section + ":" + nameof(TerminalSecurityOptions.CredentialPepper)] = pepper;
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddProvider(logs));
        services.AddSingleton(TimeProvider.System);
        services.AddAttendanceServices(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => categoryName == typeof(TerminalSecurityStartupCheck).FullName ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));

        public void Dispose()
        {
        }
    }
}
