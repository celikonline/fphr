using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Faz 3, Tur 3: MFA (TOTP) self-servis — kayıt (enroll → verify), login tamamlama (mfaRequired → complete),
/// kapatma (disable) ve hatalı kod / süresi dolmuş mfaToken kenar durumları. Algoritmanın kendisi (RFC 6238 test
/// vektörleri) <c>Sense.Modules.Identity.Tests/Security/TotpServiceTests.cs</c>'de (birim, DB'siz) doğrulanır; burada
/// yalnız uçtan uca akış (handler'lar + gerçek Postgres + AuthorizationBehaviour) test edilir.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MfaTests : IdentityStackTestBase
{
    [Fact]
    public async Task Enroll_Verify_EnablesMfa_AndLoginRequiresMfa_ThenCompleteSucceeds()
    {
        var tenantId = await SeedTenantAsync("mfa-flow");
        var email = "kerem@acme.test";
        var userId = await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);

        ActAs(tenantId, userId);
        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        enroll.Secret.ShouldNotBeNullOrWhiteSpace();
        enroll.OtpAuthUri.ShouldContain("otpauth://totp/");

        (await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)))).IsSuccess.ShouldBeTrue();

        (await ExecuteQueryAsync(new GetMeQuery())).Value.MfaEnabled.ShouldBeTrue();

        // Yeni bir "istemci" giriş dener: parola doğru ama MFA açık → token verilmeden mfaToken döner.
        var login = (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "mfa-flow", null, null))).Value;
        login.Status.ShouldBe(LoginStatuses.MfaRequired);
        login.MfaToken.ShouldNotBeNullOrWhiteSpace();
        login.Tokens.ShouldBeNull();

        var complete = await ExecuteCommandAsync(new CompleteMfaLoginCommand(login.MfaToken!, CodeFor(enroll.Secret), null, null));
        complete.IsSuccess.ShouldBeTrue(complete.IsFailure ? complete.Error.Code : null);
        complete.Value.Status.ShouldBe(LoginStatuses.Ok);
        complete.Value.Tokens.ShouldNotBeNull();
    }

    [Fact]
    public async Task VerifyMfaEnrollment_WrongCode_ReturnsInvalidCode_AndDoesNotEnable()
    {
        var tenantId = await SeedTenantAsync("mfa-verify-wrong");
        var userId = await SeedUserAsync(tenantId, "aylin@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        await ExecuteCommandAsync(new EnrollMfaCommand());

        ShouldFailWith(await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand("000000")), IdentityErrors.MfaInvalidCode, ErrorType.Validation);
        (await ExecuteQueryAsync(new GetMeQuery())).Value.MfaEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task EnrollMfa_WhenAlreadyEnabled_ReturnsConflict()
    {
        var tenantId = await SeedTenantAsync("mfa-already-on");
        var userId = await SeedUserAsync(tenantId, "burak@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));

        ShouldFailWith(await ExecuteCommandAsync(new EnrollMfaCommand()), IdentityErrors.MfaAlreadyEnabled, ErrorType.Conflict);
    }

    [Fact]
    public async Task CompleteMfaLogin_WrongCode_RecordsFailedAccess_AndEventuallyLocksOut()
    {
        var tenantId = await SeedTenantAsync("mfa-lockout");
        var email = "deniz@acme.test";
        var userId = await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));

        var login = (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "mfa-lockout", null, null))).Value;

        // MaxFailedAccessAttempts (varsayılan 5) kadar hatalı kod, aynı akışta parola hatası gibi lockout'u tetikler.
        for (var i = 0; i < IdentityDefaults.MaxFailedAccessAttempts - 1; i++)
        {
            ShouldFailWith(
                await ExecuteCommandAsync(new CompleteMfaLoginCommand(login.MfaToken!, "000000", null, null)),
                IdentityErrors.MfaInvalidCode, ErrorType.Unauthorized);
        }

        ShouldFailWith(
            await ExecuteCommandAsync(new CompleteMfaLoginCommand(login.MfaToken!, "000000", null, null)),
            IdentityErrors.MfaInvalidCode, ErrorType.Unauthorized);

        // Kilitlendikten sonra doğru kod bile CanSignIn'de (locked_out) durur.
        ShouldFailWith(
            await ExecuteCommandAsync(new CompleteMfaLoginCommand(login.MfaToken!, CodeFor(enroll.Secret), null, null)),
            IdentityErrors.LockedOut, ErrorType.Unauthorized);
    }

    [Fact]
    public async Task CompleteMfaLogin_ExpiredMfaToken_Fails()
    {
        var tenantId = await SeedTenantAsync("mfa-token-expired");
        var email = "sena@acme.test";
        var userId = await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));

        var login = (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "mfa-token-expired", null, null))).Value;

        Time.Advance(TimeSpan.FromMinutes(IdentityDefaults.MfaTokenMinutes + 1));

        ShouldFailWith(
            await ExecuteCommandAsync(new CompleteMfaLoginCommand(login.MfaToken!, CodeFor(enroll.Secret), null, null)),
            IdentityErrors.MfaTokenExpired, ErrorType.Unauthorized);
    }

    [Fact]
    public async Task DisableMfa_WithValidCode_DisablesAndLoginNoLongerRequiresMfa()
    {
        var tenantId = await SeedTenantAsync("mfa-disable");
        var email = "ela@acme.test";
        var userId = await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));

        (await ExecuteCommandAsync(new DisableMfaCommand(CodeFor(enroll.Secret)))).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new GetMeQuery())).Value.MfaEnabled.ShouldBeFalse();

        var login = (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "mfa-disable", null, null))).Value;
        login.Status.ShouldBe(LoginStatuses.Ok);
    }

    [Fact]
    public async Task GetMfaStatus_ReflectsPendingEnrollmentThenEnabled()
    {
        var tenantId = await SeedTenantAsync("mfa-status");
        var userId = await SeedUserAsync(tenantId, "gulsah@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.ShouldBe(new MfaStatusDto(false, false, false));

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.ShouldBe(new MfaStatusDto(false, true, false));

        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.ShouldBe(new MfaStatusDto(true, false, false));
    }

    [Fact]
    public async Task ResetUserMfa_ByAdmin_ClearsSecretAndDisablesMfa()
    {
        var tenantId = await SeedTenantAsync("mfa-admin-reset");
        var adminId = await SeedUserAsync(tenantId, "yonetici@acme.test", SystemRoleCodes.HR);
        var targetId = await SeedUserAsync(tenantId, "hedef@acme.test", SystemRoleCodes.Employee);

        ActAs(tenantId, targetId);
        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.Enabled.ShouldBeTrue();

        ActAs(tenantId, adminId);
        (await ExecuteCommandAsync(new ResetUserMfaCommand(targetId))).IsSuccess.ShouldBeTrue();

        ActAs(tenantId, targetId);
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.ShouldBe(new MfaStatusDto(false, false, false));
    }

    [Fact]
    public async Task ResetUserMfa_OwnAccount_ReturnsConflict()
    {
        var tenantId = await SeedTenantAsync("mfa-admin-self-reset");
        var adminId = await SeedUserAsync(tenantId, "kendi@acme.test", SystemRoleCodes.HR);
        ActAs(tenantId, adminId);

        ShouldFailWith(await ExecuteCommandAsync(new ResetUserMfaCommand(adminId)), IdentityErrors.MfaSelfResetForbidden, ErrorType.Conflict);
    }

    [Fact]
    public async Task ResetUserMfa_DifferentTenantUser_ReturnsNotFound()
    {
        var tenantAId = await SeedTenantAsync("mfa-admin-reset-tenant-a");
        var tenantBId = await SeedTenantAsync("mfa-admin-reset-tenant-b");
        var adminId = await SeedUserAsync(tenantAId, "yonetici-a@acme.test", SystemRoleCodes.HR);
        var otherTenantUserId = await SeedUserAsync(tenantBId, "kullanici-b@acme.test", SystemRoleCodes.Employee);

        ActAs(tenantAId, adminId);
        ShouldFailWith(await ExecuteCommandAsync(new ResetUserMfaCommand(otherTenantUserId)), IdentityErrors.UserNotFound, ErrorType.NotFound);
    }

    [Fact]
    public async Task DisableMfa_WrongCode_Fails_AndMfaStaysEnabled()
    {
        var tenantId = await SeedTenantAsync("mfa-disable-wrong");
        var userId = await SeedUserAsync(tenantId, "tolga@acme.test", SystemRoleCodes.Employee);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));

        ShouldFailWith(await ExecuteCommandAsync(new DisableMfaCommand("000000")), IdentityErrors.MfaInvalidCode, ErrorType.Validation);
        (await ExecuteQueryAsync(new GetMeQuery())).Value.MfaEnabled.ShouldBeTrue();
    }

    /// <summary>FG-022 (olumsuz): politikanın zorunlu kıldığı roldeki kullanıcı MFA'yı doğru kodla bile kapatamaz.</summary>
    [Fact]
    public async Task DisableMfa_RoleRequiredByPolicy_ReturnsRequiredByPolicy_AndMfaStaysEnabled()
    {
        var tenantId = await SeedTenantAsync("mfa-policy-disable");
        var userId = await SeedUserAsync(tenantId, "politika@acme.test", SystemRoleCodes.HR);
        await SetSecurityPolicyAsync(tenantId, SystemRoleCodes.HR);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.ShouldBe(new MfaStatusDto(true, false, true));

        ShouldFailWith(await ExecuteCommandAsync(new DisableMfaCommand(CodeFor(enroll.Secret))), IdentityErrors.MfaRequiredByPolicy, ErrorType.Validation);
        (await ExecuteQueryAsync(new GetMeQuery())).Value.MfaEnabled.ShouldBeTrue();
    }

    /// <summary>FG-022: politika başka bir rolü zorunlu kılıyorsa kullanıcı MFA'yı kapatabilir.</summary>
    [Fact]
    public async Task DisableMfa_PolicyForOtherRole_Disables()
    {
        var tenantId = await SeedTenantAsync("mfa-policy-other-role");
        var userId = await SeedUserAsync(tenantId, "serbest@acme.test", SystemRoleCodes.Employee);
        await SetSecurityPolicyAsync(tenantId, SystemRoleCodes.HR);
        ActAs(tenantId, userId);

        var enroll = (await ExecuteCommandAsync(new EnrollMfaCommand())).Value;
        await ExecuteCommandAsync(new VerifyMfaEnrollmentCommand(CodeFor(enroll.Secret)));
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.RequiredByPolicy.ShouldBeFalse();

        (await ExecuteCommandAsync(new DisableMfaCommand(CodeFor(enroll.Secret)))).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new GetMfaStatusQuery())).Value.ShouldBe(new MfaStatusDto(false, false, false));
    }

    private async Task SetSecurityPolicyAsync(Guid tenantId, params string[] roleCodes)
    {
        var db = Get<IdentityDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
            tenant.UpdateSecurityPolicy(new TenantSecurityPolicy { RequireMfaForRoles = roleCodes });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// RFC 6238'i (30 sn adım, HMAC-SHA1, 6 hane) test kliyentinin yerine getirir — üretim <c>TotpService</c> ile
    /// bit bit aynı algoritma, ayrı bir implementasyon (o sınıfın Base32/ComputeCode'u `internal`, test projesine görünmez).
    /// Testin kendi `Time` (MutableTimeProvider) saatine göre kod üretir.
    /// </summary>
    private string CodeFor(string secretBase32)
    {
        var counter = Time.GetUtcNow().ToUnixTimeSeconds() / 30;
        var key = Base32Decode(secretBase32);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

#pragma warning disable CA5350 // RFC 6238 TOTP HMAC-SHA1 gerektirir (TotpService ile aynı)
        var hash = HMACSHA1.HashData(key, counterBytes);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(6, '0');
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(base32.Length * 5 / 8);
        int buffer = 0, bitsInBuffer = 0;
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | index;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                bytes.Add((byte)((buffer >> bitsInBuffer) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
