using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Auth;

/// <summary>
/// FG-022: kiracı güvenlik politikası (RequireMfaForRoles ∩ kullanıcı rolleri) self-servis MFA kapatmayı engeller ve
/// güvenlik ekranı durumunda <see cref="MfaStatusDto.RequiredByPolicy"/> olarak görünür.
/// </summary>
public sealed class MfaPolicyHandlerTests
{
    private const string EncryptedSecret = "encrypted-secret";
    private const string PlainSecret = "PLAINSECRET";
    private const string ValidCode = "123456";

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly ITotpService _totp = Substitute.For<ITotpService>();
    private readonly IEncryptionProvider _encryption = Substitute.For<IEncryptionProvider>();

    private User Arrange(string roleCode, IReadOnlyList<string> enforcedRoles, bool mfaEnabled)
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        tenant.UpdateSecurityPolicy(new TenantSecurityPolicy { RequireMfaForRoles = enforcedRoles });
        var role = Role.CreateSystem(tenant.Id, roleCode);
        var user = User.Create(tenant.Id, "kullanici@example.com", "Deneme", "Kullanıcı", "tr").Value;
        user.SetRoles([role.Id]);
        if (mfaEnabled)
        {
            user.EnableMfa(EncryptedSecret);
        }

        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(user.Id);
        _tenantContext.TenantId.Returns(tenant.Id);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _roles.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns([role]);
        _encryption.Decrypt(EncryptedSecret).Returns(PlainSecret);
        _totp.ValidateCode(PlainSecret, ValidCode).Returns(true);
        return user;
    }

    private GetMfaStatusHandler StatusHandler() => new(_currentUser, _tenantContext, _users, _tenants, _roles);

    private DisableMfaHandler DisableHandler() => new(_currentUser, _tenantContext, _users, _tenants, _roles, _totp, _encryption);

    [Fact]
    public async Task GetMfaStatus_RoleEnforcedByPolicy_ReturnsRequiredByPolicyTrue()
    {
        Arrange(SystemRoleCodes.HR, [SystemRoleCodes.HR], mfaEnabled: true);

        var result = await StatusHandler().Handle(new GetMfaStatusQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new MfaStatusDto(Enabled: true, PendingEnrollment: false, RequiredByPolicy: true));
    }

    [Fact]
    public async Task GetMfaStatus_RoleNotEnforced_ReturnsRequiredByPolicyFalse()
    {
        Arrange(SystemRoleCodes.Employee, [SystemRoleCodes.Owner], mfaEnabled: false);

        var result = await StatusHandler().Handle(new GetMfaStatusQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new MfaStatusDto(Enabled: false, PendingEnrollment: false, RequiredByPolicy: false));
    }

    [Fact]
    public async Task DisableMfa_RoleEnforcedByPolicy_FailsWithRequiredByPolicy_AndKeepsMfaEnabled()
    {
        var user = Arrange(SystemRoleCodes.PayrollSpecialist, [SystemRoleCodes.PayrollSpecialist], mfaEnabled: true);

        var result = await DisableHandler().Handle(new DisableMfaCommand(ValidCode), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.MfaRequiredByPolicy);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        user.MfaEnabled.ShouldBeTrue();
        user.TotpSecretEncrypted.ShouldBe(EncryptedSecret);
    }

    [Fact]
    public async Task DisableMfa_NoPolicyForRole_WithValidCode_Disables()
    {
        var user = Arrange(SystemRoleCodes.Employee, [SystemRoleCodes.Owner], mfaEnabled: true);

        var result = await DisableHandler().Handle(new DisableMfaCommand(ValidCode), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.MfaEnabled.ShouldBeFalse();
        user.TotpSecretEncrypted.ShouldBeNull();
    }

    [Fact]
    public async Task DisableMfa_NoPolicyForRole_WrongCode_StillFailsWithInvalidCode()
    {
        var user = Arrange(SystemRoleCodes.Employee, [], mfaEnabled: true);

        var result = await DisableHandler().Handle(new DisableMfaCommand("000000"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.MfaInvalidCode);
        user.MfaEnabled.ShouldBeTrue();
    }
}
