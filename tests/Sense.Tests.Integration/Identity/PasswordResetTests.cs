using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Faz 3: "şifremi unuttum" / şifre sıfırlama. Kullanıcı numaralandırmasına izin vermez (var olmayan/pasif hesap
/// için de aynı başarı yanıtı), token tek kullanımlıktır ve süresi dolar, başarılı sıfırlama açık oturumları kapatır.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PasswordResetTests : IdentityStackTestBase
{
    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsSuccess_WithoutSendingMail()
    {
        var result = await ExecuteCommandAsync(new ForgotPasswordCommand("kimse@yok.test"));

        result.IsSuccess.ShouldBeTrue();
        Mailer.PasswordResets.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForgotPassword_InvitedUser_ReturnsSuccess_WithoutSendingMail()
    {
        var tenantId = await SeedTenantAsync("reset-invited");
        var email = "davetli@acme.test";
        await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee, active: false);

        var result = await ExecuteCommandAsync(new ForgotPasswordCommand(email));

        result.IsSuccess.ShouldBeTrue();
        Mailer.PasswordResets.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForgotPassword_ActiveUser_SendsResetLink_AndResetPassword_AllowsNewPassword_AndRevokesOldSessions()
    {
        var tenantId = await SeedTenantAsync("reset-flow");
        var email = "kerem@acme.test";
        await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);

        // Var olan oturumun (refresh token) sıfırlama sonrası geçersiz kalacağını doğrulamak için önce giriş yapılır.
        var login = (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "reset-flow", null, null))).Value;
        login.Status.ShouldBe(LoginStatuses.Ok);
        var oldRefreshToken = login.Tokens!.RefreshToken;

        var forgot = await ExecuteCommandAsync(new ForgotPasswordCommand(email));
        forgot.IsSuccess.ShouldBeTrue();
        var sent = Mailer.PasswordResets.ShouldHaveSingleItem();
        sent.To.ShouldBe(email);
        var token = TokenOf(sent.Url);

        var reset = await ExecuteCommandAsync(new ResetPasswordCommand(token, "YeniParola-2026"));
        reset.IsSuccess.ShouldBeTrue(reset.IsFailure ? reset.Error.Code : null);

        // Eski parola artık çalışmaz, yenisi çalışır.
        ShouldFailWith(await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "reset-flow", null, null)), IdentityErrors.InvalidCredentials, ErrorType.Unauthorized);
        var relogin = (await ExecuteCommandAsync(new LoginCommand(email, "YeniParola-2026", "reset-flow", null, null))).Value;
        relogin.Status.ShouldBe(LoginStatuses.Ok);

        // Sıfırlamadan önceki oturum (refresh token) kapatılmıştır.
        ShouldFailWith(await ExecuteCommandAsync(new RefreshTokenCommand(oldRefreshToken, null, null)), IdentityErrors.RefreshTokenReused, ErrorType.Unauthorized);

        // Token tek kullanımlıktır: aynı token'la ikinci sıfırlama denemesi başarısız olur.
        ShouldFailWith(await ExecuteCommandAsync(new ResetPasswordCommand(token, "BaskaParola-2026")), IdentityErrors.PasswordResetTokenExpired, ErrorType.Rule);
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_Fails()
    {
        var tenantId = await SeedTenantAsync("reset-expired");
        var email = "expired@acme.test";
        await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);

        await ExecuteCommandAsync(new ForgotPasswordCommand(email));
        var token = TokenOf(Mailer.PasswordResets.ShouldHaveSingleItem().Url);

        Time.Advance(TimeSpan.FromMinutes(IdentityDefaults.PasswordResetValidityMinutes + 1));

        ShouldFailWith(await ExecuteCommandAsync(new ResetPasswordCommand(token, "YeniParola-2026")), IdentityErrors.PasswordResetTokenExpired, ErrorType.Rule);
    }

    [Fact]
    public async Task ResetPassword_UnknownToken_Fails() =>
        ShouldFailWith(await ExecuteCommandAsync(new ResetPasswordCommand("gecersiz-token", "YeniParola-2026")), IdentityErrors.PasswordResetTokenNotFound, ErrorType.NotFound);

    private static string TokenOf(string url) => Uri.UnescapeDataString(url[(url.LastIndexOf('/') + 1)..]);
}
