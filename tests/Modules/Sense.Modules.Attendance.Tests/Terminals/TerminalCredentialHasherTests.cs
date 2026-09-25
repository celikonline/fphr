using System.Reflection;
using Microsoft.Extensions.Options;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Terminals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Kimlik değeri (kart/PIN) düz metin saklanmaz: pepper'lı HMAC-SHA256 özeti; denetim alan listelerinde yok; pepper yoksa anlaşılır hata.</summary>
public sealed class TerminalCredentialHasherTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public void Credential_value_never_plaintext_in_db_or_audit()
    {
        var hasher = TerminalTestData.Hasher();

        var digest = hasher.Hash(TenantA, TerminalCredentialKind.Pin, "1234");

        digest.Length.ShouldBe(64);
        digest.ShouldMatch("^[0-9a-f]{64}$");
        digest.ShouldNotContain("1234");
        digest.Length.ShouldBeLessThanOrEqualTo(EmployeeTerminalCredential.ValueMaxLength);
        EmployeeTerminalCredential.Create(TenantA, Guid.NewGuid(), TerminalCredentialKind.Pin, digest).Value.Value.ShouldBe(digest);

        // Denetim: interceptor yalnız AuditedFields'ı diff'ler; kimlik değeri ve anahtar özeti/öneki hiçbir listede yok.
        AuditedFieldsOf(typeof(EmployeeTerminalCredential)).ShouldNotContain(nameof(EmployeeTerminalCredential.Value));
        AuditedFieldsOf(typeof(Terminal)).ShouldNotContain(nameof(Terminal.ApiKeyHash));
        AuditedFieldsOf(typeof(Terminal)).ShouldNotContain(nameof(Terminal.ApiKeyPrefix));
        EmployeeTerminalCredential.SensitiveFields.ShouldContain(nameof(EmployeeTerminalCredential.Value));
    }

    [Fact]
    public void Digest_is_deterministic_and_bound_to_tenant_kind_and_pepper()
    {
        var hasher = TerminalTestData.Hasher();
        var digest = hasher.Hash(TenantA, TerminalCredentialKind.Card, "04A1B2C3");

        hasher.Hash(TenantA, TerminalCredentialKind.Card, "04A1B2C3").ShouldBe(digest);
        hasher.Hash(TenantA, TerminalCredentialKind.Card, " 04a1b2c3 ").ShouldBe(digest);
        hasher.Hash(TenantB, TerminalCredentialKind.Card, "04A1B2C3").ShouldNotBe(digest);
        hasher.Hash(TenantA, TerminalCredentialKind.ExternalId, "04A1B2C3").ShouldNotBe(digest);
        hasher.Hash(TenantA, TerminalCredentialKind.ExternalId, "Abc").ShouldNotBe(hasher.Hash(TenantA, TerminalCredentialKind.ExternalId, "abc"));
        TerminalTestData.Hasher(TerminalTestData.TestPepper + "-other").Hash(TenantA, TerminalCredentialKind.Card, "04A1B2C3").ShouldNotBe(digest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-pepper")]
    public void Missing_or_short_pepper_fails_with_a_clear_message(string? pepper)
    {
        var options = new TerminalSecurityOptions { CredentialPepper = pepper };

        var result = new TerminalSecurityOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Attendance:Terminals:CredentialPepper");
        Should.Throw<InvalidOperationException>(() => new HmacTerminalCredentialHasher(Options.Create(options)));
    }

    [Fact]
    public void Configured_pepper_passes_validation()
    {
        new TerminalSecurityOptionsValidator().Validate(null, new TerminalSecurityOptions { CredentialPepper = TerminalTestData.TestPepper })
            .Succeeded.ShouldBeTrue();
    }

    private static IReadOnlyList<string> AuditedFieldsOf(Type type) =>
        type.GetProperty("AuditedFields", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IReadOnlyList<string> ?? [];
}
