using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Application.Users;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>Regresyon: davet kabulünde <c>Password12</c> gibi zayıf/yaygın parolalar kabul ediliyordu (kural yalnız uzunluktu).</summary>
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Password12")]
    [InlineData("Password-12345")]
    [InlineData("PASSWORD1234!")]
    [InlineData("Qwerty12345!")]
    [InlineData("Demo123!Demo")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("12345678901")]
    [InlineData("onlylowercaseletters")]
    [InlineData("lowerand123456")]
    [InlineData("UPPERAND123456")]
    public void WeakPasswords_AreRejected(string password) =>
        PasswordPolicy.IsStrongEnough(password).ShouldBeFalse();

    [Theory]
    [InlineData("Gunes-Deniz-2026")]
    [InlineData("kediler.Kosar.7")]
    [InlineData("Mavi Bulut 42")]
    [InlineData("Şeker+Portakal9")]
    public void StrongPasswords_AreAccepted(string password) =>
        PasswordPolicy.IsStrongEnough(password).ShouldBeTrue();

    [Theory]
    [InlineData("Kerem.Aydin-2026x", "kerem.aydin@acme.test", true)]
    [InlineData("KEREM.AYDIN-2026x", "kerem.aydin@acme.test", true)]
    [InlineData("Gunes-Deniz-2026", "kerem.aydin@acme.test", false)]
    [InlineData("ab-Gunes-2026", "ab@acme.test", false)]
    [InlineData("Gunes-Deniz-2026", null, false)]
    public void EmailLocalPart_IsDetected(string password, string? email, bool expected) =>
        PasswordPolicy.ContainsEmailLocalPart(password, email).ShouldBe(expected);

    [Fact]
    public void AcceptInvitationValidator_RejectsWeakPassword_WithTooWeakCode()
    {
        var command = new AcceptInvitationCommand("token", "Password12", true, IdentityDefaults.KnownPrivacyNoticeVersion, null, null);

        var result = new AcceptInvitationValidator(Options.Create(new IdentityOptions())).Validate(command);

        result.Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.PasswordTooWeak);
    }

    [Fact]
    public void ResetPasswordValidator_RejectsWeak_AndAcceptsStrong()
    {
        var validator = new ResetPasswordValidator(Options.Create(new IdentityOptions()));

        validator.Validate(new ResetPasswordCommand("t", "Password12")).Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.PasswordTooWeak);
        validator.Validate(new ResetPasswordCommand("t", "Gunes-Deniz-2026")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ResetPasswordValidator_TooShort_ReportsOnlyTooShort() =>
        new ResetPasswordValidator(Options.Create(new IdentityOptions())).Validate(new ResetPasswordCommand("t", "Ab1!"))
            .Errors.Select(e => e.ErrorMessage).ShouldBe([IdentityErrors.PasswordTooShort]);

    [Fact]
    public void CreateTenantValidator_RejectsPasswordContainingOwnerEmailLocalPart()
    {
        var validator = new CreateTenantValidator(Options.Create(new IdentityOptions()));
        var command = new CreateTenantCommand("Acme", "acme", "kerem.aydin@acme.test", "Kerem", "Aydin", TenantOwnerMode.Password, "Kerem.Aydin-2026x", null, null);

        validator.Validate(command).Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.PasswordTooWeak);
    }

    [Fact]
    public void CreateTenantValidator_InviteMode_DoesNotRequirePassword()
    {
        var validator = new CreateTenantValidator(Options.Create(new IdentityOptions()));
        var command = new CreateTenantCommand("Acme", "acme", "kerem@acme.test", "Kerem", "Aydin", TenantOwnerMode.Invite, null, null, null);

        validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void CreateTenantValidator_PasswordMode_RequiresPassword()
    {
        var validator = new CreateTenantValidator(Options.Create(new IdentityOptions()));
        var command = new CreateTenantCommand("Acme", "acme", "kerem@acme.test", "Kerem", "Aydin", TenantOwnerMode.Password, null, null, null);

        validator.Validate(command).Errors.ShouldContain(e => e.ErrorMessage == IdentityErrors.TenantOwnerPasswordRequired);
    }
}
