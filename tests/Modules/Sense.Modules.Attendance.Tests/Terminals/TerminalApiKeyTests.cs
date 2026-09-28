using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Terminal API anahtarı (Z5.1): üretim, yalnız özet saklama, sabit süreli çok adaylı doğrulama, pasif terminal, yenileme.</summary>
public sealed class TerminalApiKeyTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ILocationLookup _locations = Substitute.For<ILocationLookup>();

    public TerminalApiKeyTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync(default!, default).ReturnsForAnyArgs([]);
    }

    [Fact]
    public async Task Key_shown_once_and_only_hash_stored()
    {
        Terminal? stored = null;
        _terminals.AddTerminal(Arg.Do<Terminal>(t => stored = t));

        var result = await new CreateTerminalHandler(_terminals, _locations, _tenant).Handle(
            new CreateTerminalCommand(new TerminalInput("T-01", "Ana giriş", null, null, TerminalDirectionMode.InOnly)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var key = result.Value.ApiKey;
        TerminalApiKey.IsWellFormed(key).ShouldBeTrue();
        Convert.FromBase64String(key.Replace('-', '+').Replace('_', '/') + "=").Length.ShouldBe(TerminalApiKey.KeyBytes);
        result.Value.ApiKeyPrefix.ShouldBe(key[..TerminalApiKey.PrefixLength]);
        result.Value.ToString().ShouldNotContain(key);

        stored.ShouldNotBeNull();
        stored.ApiKeyHash.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))));
        stored.ApiKeyHash.ShouldNotContain(key);
        stored.ApiKeyPrefix.ShouldBe(key[..TerminalApiKey.PrefixLength]);

        // Sonraki okumalar anahtarı ya da özetini asla döndürmez.
        _terminals.GetTerminalByIdAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);
        var dto = (await new GetTerminalHandler(_terminals, TerminalTestData.Monitor(TimeProvider.System), TimeProvider.System).Handle(new GetTerminalQuery(stored.Id), TestContext.Current.CancellationToken)).Value;
        typeof(TerminalDto).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Hash") || n == "ApiKey");
        dto.ToString().ShouldNotContain(key);
        dto.ToString().ShouldNotContain(stored.ApiKeyHash);
    }

    [Fact]
    public void Generated_keys_are_unique_and_material_does_not_print_the_key()
    {
        var keys = Enumerable.Range(0, 50).Select(_ => TerminalApiKey.Generate()).ToList();

        keys.Select(k => k.Key).Distinct().Count().ShouldBe(50);
        keys.ShouldAllBe(k => k.Key.Length == TerminalApiKey.EncodedLength && k.Hash.Length == 64);
        keys[0].ToString().ShouldNotContain(keys[0].Key);
    }

    [Fact]
    public async Task Prefix_collision_uses_constant_time_compare_over_all_candidates()
    {
        var tenantB = Guid.NewGuid();
        var keyA = TerminalTestData.KeyWithPrefix("Samepfx0");
        var keyB = TerminalTestData.KeyWithPrefix("Samepfx0");
        var terminalA = TerminalTestData.NewTerminal(_tenantId, keyA, TerminalDirectionMode.InOnly);
        var terminalB = TerminalTestData.NewTerminal(tenantB, keyB, TerminalDirectionMode.OutOnly);
        _terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync("Samepfx0", Arg.Any<CancellationToken>()).Returns([terminalA, terminalB]);
        var authenticator = new TerminalKeyAuthenticator(_terminals);

        var b = await authenticator.AuthenticateAsync(keyB, TestContext.Current.CancellationToken);
        var a = await authenticator.AuthenticateAsync(keyA, TestContext.Current.CancellationToken);
        var neither = await authenticator.AuthenticateAsync(TerminalTestData.KeyWithPrefix("Samepfx0"), TestContext.Current.CancellationToken);

        b.ShouldBe(new TerminalIdentity(terminalB.Id, tenantB, TerminalDirectionMode.OutOnly, null, "T-01"));
        a.ShouldBe(new TerminalIdentity(terminalA.Id, _tenantId, TerminalDirectionMode.InOnly, null, "T-01"));
        neither.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("Samepfx0!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task Malformed_key_is_rejected_without_touching_the_database(string? key)
    {
        var identity = await new TerminalKeyAuthenticator(_terminals).AuthenticateAsync(key, TestContext.Current.CancellationToken);

        identity.ShouldBeNull();
        await _terminals.DidNotReceiveWithAnyArgs().ListActiveByApiKeyPrefixAcrossTenantsAsync(default!, default);
    }

    [Fact]
    public async Task Unknown_key_without_candidates_is_rejected()
    {
        var identity = await new TerminalKeyAuthenticator(_terminals).AuthenticateAsync(TerminalApiKey.Generate().Key, TestContext.Current.CancellationToken);

        identity.ShouldBeNull();
    }

    [Fact]
    public async Task Inactive_terminal_rejected()
    {
        var key = TerminalApiKey.Generate().Key;
        var terminal = TerminalTestData.NewTerminal(_tenantId, key);
        terminal.Deactivate();
        // Gerçek depo pasifleri zaten döndürmez; eşleşen aday pasifse de (savunma katmanı) reddedilir.
        _terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync(TerminalApiKey.PrefixOf(key), Arg.Any<CancellationToken>()).Returns([terminal]);

        (await new TerminalKeyAuthenticator(_terminals).AuthenticateAsync(key, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Key_rotation_invalidates_old_key()
    {
        var oldKey = TerminalApiKey.Generate().Key;
        var terminal = TerminalTestData.NewTerminal(_tenantId, oldKey);
        _terminals.GetTerminalByIdAsync(terminal.Id, Arg.Any<CancellationToken>()).Returns(terminal);
        _terminals.ListActiveByApiKeyPrefixAcrossTenantsAsync(default!, default).ReturnsForAnyArgs(_ => [terminal]);
        var authenticator = new TerminalKeyAuthenticator(_terminals);
        (await authenticator.AuthenticateAsync(oldKey, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var rotated = await new RotateTerminalApiKeyHandler(_terminals).Handle(new RotateTerminalApiKeyCommand(terminal.Id), TestContext.Current.CancellationToken);

        rotated.IsSuccess.ShouldBeTrue();
        rotated.Value.ApiKey.ShouldNotBe(oldKey);
        terminal.ApiKeyHash.ShouldBe(TerminalApiKey.Hash(rotated.Value.ApiKey));
        (await authenticator.AuthenticateAsync(oldKey, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await authenticator.AuthenticateAsync(rotated.Value.ApiKey, TestContext.Current.CancellationToken))!.TerminalId.ShouldBe(terminal.Id);
    }
}
