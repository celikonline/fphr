using System.Reflection;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Terminals;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;
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

        var digest = hasher.Digest(TenantA, TerminalCredentialKind.Pin, "1234");

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
        var digest = hasher.Digest(TenantA, TerminalCredentialKind.Card, "04A1B2C3");

        hasher.Digest(TenantA, TerminalCredentialKind.Card, "04A1B2C3").ShouldBe(digest);
        hasher.Digest(TenantA, TerminalCredentialKind.Card, " 04a1b2c3 ").ShouldBe(digest);
        hasher.Digest(TenantB, TerminalCredentialKind.Card, "04A1B2C3").ShouldNotBe(digest);
        hasher.Digest(TenantA, TerminalCredentialKind.ExternalId, "04A1B2C3").ShouldNotBe(digest);
        hasher.Digest(TenantA, TerminalCredentialKind.ExternalId, "Abc").ShouldNotBe(hasher.Digest(TenantA, TerminalCredentialKind.ExternalId, "abc"));
        TerminalTestData.Hasher(TerminalTestData.TestPepper + "-other").Digest(TenantA, TerminalCredentialKind.Card, "04A1B2C3").ShouldNotBe(digest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-pepper")]
    public void Credential_ops_return_503_without_pepper(string? pepper)
    {
        var hasher = TerminalTestData.Hasher(pepper);

        var result = hasher.Hash(TenantA, TerminalCredentialKind.Pin, "1234");

        hasher.IsAvailable.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.ServiceUnavailable);
        result.Error.Code.ShouldBe(TerminalErrors.CredentialPepperMissing);
        result.Error.Args.ShouldBeNull();
        HttpStatusMap.For(result.Error.Type).ShouldBe(503);
        ProblemTitleKeys.For(result.Error.Type).ShouldBe(ProblemTitleKeys.ServiceUnavailable);
    }

    [Fact]
    public void Configured_pepper_is_available()
    {
        TerminalTestData.Hasher().IsAvailable.ShouldBeTrue();
        new TerminalSecurityOptions { CredentialPepper = "  " + TerminalTestData.TestPepper + "  " }.UsablePepper.ShouldBe(TerminalTestData.TestPepper);
    }

    private static IReadOnlyList<string> AuditedFieldsOf(Type type) =>
        type.GetProperty("AuditedFields", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IReadOnlyList<string> ?? [];
}
