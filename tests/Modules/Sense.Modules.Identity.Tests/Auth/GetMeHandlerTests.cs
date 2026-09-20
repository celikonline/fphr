using NSubstitute;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Auth;

/// <summary>
/// faz0-durum-takibi.md #11: impersonation oturumunda `/identity/me` 401 dönüyordu çünkü JWT'nin `sub`'ı
/// (platform kullanıcısının id'si) yalnız PLATFORM kiracısında bir satır — hedef kiracının tenant filtresiyle
/// hiç bulunamıyordu. GetMeHandler artık aktif bir impersonation oturumunda bu satırı kiracı filtresi olmadan
/// (<see cref="IUserRepository.GetByIdAcrossTenantsAsync"/>) okuyor ve AuthorizationBehaviour'ın zaten uyguladığı
/// "tam erişim" bypass'ını MeDto'ya da yansıtıyor (Roles/Permissions boş, AccessSettingsBypass=true).
/// </summary>
public sealed class GetMeHandlerTests
{
    private static GetMeHandler HandlerWith(
        ICurrentUser currentUser, ITenantContext tenantContext, IUserRepository users, ITenantRepository tenants,
        IRoleRepository roles, IPermissionService permissions, IImpersonationSessionValidator? sessions) =>
        new(currentUser, tenantContext, users, tenants, roles, permissions, sessions);

    [Fact]
    public async Task Handle_ActiveImpersonationSession_ReturnsPlatformUserAcrossTenants_NoPermissionLookup()
    {
        var platformTenant = Tenant.Create("sense-platform", "Sense", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        var platformUser = User.Create(platformTenant.Id, "destek@sense.com", "Sense", "Destek", "tr").Value;
        var targetTenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(platformUser.Id);
        currentUser.ImpersonatorUserId.Returns(platformUser.Id);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(true);
        tenantContext.TenantId.Returns(targetTenant.Id);

        var users = Substitute.For<IUserRepository>();
        // Normal (tenant-filtered) lookup must NOT be used for the impersonation branch - it would find nothing.
        users.GetByIdAsync(platformUser.Id, Arg.Any<CancellationToken>()).Returns((User?)null);
        users.GetByIdAcrossTenantsAsync(platformUser.Id, Arg.Any<CancellationToken>()).Returns(platformUser);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(targetTenant.Id, Arg.Any<CancellationToken>()).Returns(targetTenant);

        var roles = Substitute.For<IRoleRepository>();
        var permissions = Substitute.For<IPermissionService>();
        var sessions = Substitute.For<IImpersonationSessionValidator>();
        sessions.IsActiveAsync(Arg.Any<CancellationToken>()).Returns(true);

        var handler = HandlerWith(currentUser, tenantContext, users, tenants, roles, permissions, sessions);

        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(platformUser.Id);
        result.Value.Tenant.Id.ShouldBe(targetTenant.Id);
        result.Value.Roles.ShouldBeEmpty();
        result.Value.Permissions.ShouldBeEmpty();
        result.Value.Features.AccessSettingsBypass.ShouldBeTrue();
        await roles.DidNotReceive().GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        await permissions.DidNotReceive().GetPermissionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ImpersonatorClaimButSessionNoLongerActive_FallsBackToNormalLookup_NotFoundIs401()
    {
        var platformUser = User.Create(Guid.NewGuid(), "destek@sense.com", "Sense", "Destek", "tr").Value;
        var targetTenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(platformUser.Id);
        currentUser.ImpersonatorUserId.Returns(platformUser.Id);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(true);
        tenantContext.TenantId.Returns(targetTenant.Id);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(platformUser.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(targetTenant.Id, Arg.Any<CancellationToken>()).Returns(targetTenant);

        var sessions = Substitute.For<IImpersonationSessionValidator>();
        sessions.IsActiveAsync(Arg.Any<CancellationToken>()).Returns(false);

        var handler = HandlerWith(currentUser, tenantContext, users, tenants, Substitute.For<IRoleRepository>(), Substitute.For<IPermissionService>(), sessions);

        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await users.DidNotReceive().GetByIdAcrossTenantsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoImpersonationValidatorRegistered_FallsBackToNormalLookup()
    {
        var platformUser = User.Create(Guid.NewGuid(), "eren@example.com", "Eren", "Yılmaz", "tr").Value;
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(platformUser.Id);
        currentUser.ImpersonatorUserId.Returns(platformUser.Id);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(true);
        tenantContext.TenantId.Returns(tenant.Id);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(platformUser.Id, Arg.Any<CancellationToken>()).Returns(platformUser);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var roles = Substitute.For<IRoleRepository>();
        roles.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        var permissions = Substitute.For<IPermissionService>();
        permissions.GetPermissionsAsync(platformUser.Id, Arg.Any<CancellationToken>()).Returns(new HashSet<string>());

        // No IImpersonationSessionValidator registered (e.g. a host that never wires one) -> null, same as
        // AuthorizationBehaviour's own optional dependency default.
        var handler = HandlerWith(currentUser, tenantContext, users, tenants, roles, permissions, null);

        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(platformUser.Id);
    }

    /// <summary>
    /// Ortak fikstür: normal (impersonation olmayan) giriş için tenant + rol + kullanıcı grafiğini kurar.
    /// senseik-051 - tenant.SecurityPolicy çağıran tarafından ayarlanır, sonra Handle() ile Features.MfaSetupRequired doğrulanır.
    /// </summary>
    private static GetMeHandler NormalHandlerFor(Tenant tenant, User user, Role role, out ITenantContext tenantContext)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(user.Id);
        currentUser.ImpersonatorUserId.Returns((Guid?)null);

        tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(true);
        tenantContext.TenantId.Returns(tenant.Id);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var roles = Substitute.For<IRoleRepository>();
        roles.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns([role]);

        var permissions = Substitute.For<IPermissionService>();
        permissions.GetPermissionsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        permissions.GetScopeAsync(user.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(DataScope.Self);

        return HandlerWith(currentUser, tenantContext, users, tenants, roles, permissions, sessions: null);
    }

    [Fact]
    public async Task Handle_UserRoleEnforcedByPolicy_MfaDisabled_SetsMfaSetupRequiredTrue()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        tenant.UpdateSecurityPolicy(new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.PayrollSpecialist] });

        var role = Role.CreateSystem(tenant.Id, SystemRoleCodes.PayrollSpecialist);
        var user = User.Create(tenant.Id, "bordro@example.com", "Bordro", "Uzmanı", "tr").Value;
        user.SetRoles([role.Id]);
        // MfaEnabled false (varsayılan) - zorunlu rol + kapalı MFA -> kurulum akışına yönlendirilmeli.

        var handler = NormalHandlerFor(tenant, user, role, out _);
        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Features.MfaSetupRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_UserRoleEnforcedByPolicy_MfaAlreadyEnabled_SetsMfaSetupRequiredFalse()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        tenant.UpdateSecurityPolicy(new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.Owner] });

        var role = Role.CreateSystem(tenant.Id, SystemRoleCodes.Owner);
        var user = User.Create(tenant.Id, "sahip@example.com", "Hesap", "Sahibi", "tr").Value;
        user.SetRoles([role.Id]);
        user.BeginMfaEnrollment("encrypted-secret");
        user.EnableMfa("encrypted-secret");

        var handler = NormalHandlerFor(tenant, user, role, out _);
        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MfaEnabled.ShouldBeTrue();
        result.Value.Features.MfaSetupRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_PolicyDoesNotEnforceCallersRole_SetsMfaSetupRequiredFalse()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        tenant.UpdateSecurityPolicy(new TenantSecurityPolicy { RequireMfaForRoles = [SystemRoleCodes.Owner] });

        var role = Role.CreateSystem(tenant.Id, SystemRoleCodes.Employee);
        var user = User.Create(tenant.Id, "calisan@example.com", "Çalışan", "Kullanıcı", "tr").Value;
        user.SetRoles([role.Id]);

        var handler = NormalHandlerFor(tenant, user, role, out _);
        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Features.MfaSetupRequired.ShouldBeFalse();
    }

    /// <summary>senseik-057: kiracının kapattığı modüller /me features.disabledModules ile herkese (bypass yok) döner.</summary>
    [Fact]
    public async Task Handle_TenantDisabledModules_AreExposedInFeatures_EvenForBypassRoles()
    {
        var tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        tenant.UpdateModuleSettings(TenantModuleSettings.WithDisabled(["payroll", "leave"]));

        var role = Role.CreateSystem(tenant.Id, SystemRoleCodes.Owner);
        var user = User.Create(tenant.Id, "sahip@example.com", "Hesap", "Sahibi", "tr").Value;
        user.SetRoles([role.Id]);

        var handler = NormalHandlerFor(tenant, user, role, out _);
        var result = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Features.DisabledModules.ShouldBe(["leave", "payroll"]);
    }
}
