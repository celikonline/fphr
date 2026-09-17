using Microsoft.Extensions.Logging;
using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>
/// G-3-19 ("tur içi ek iş" 18:55): yönetici MFA sıfırladığında hedef kullanıcıya güvenlik bildirimi yalnız MFA
/// gerçekten açıkken gönderilir; kendi hesabına 409 döndüğünde mailer hiç çağrılmaz.
/// </summary>
public sealed class ResetUserMfaHandlerTests
{
    private static ResetUserMfaHandler Handler(
        IUserRepository users,
        ITenantRepository tenants,
        ITenantContext tenant,
        ICurrentUser currentUser,
        IIdentityMailer mailer) =>
        new(users, tenants, tenant, currentUser, mailer, Substitute.For<ILogger<ResetUserMfaHandler>>());

    private static User MfaEnabledUser(Guid tenantId)
    {
        var user = User.Create(tenantId, "tolga@acme.test", "Tolga", "Yıldız", "tr").Value;
        user.EnableMfa("encrypted-secret");
        return user;
    }

    [Fact]
    public async Task Handle_MfaEnabledUser_DisablesMfa_AndSendsNotice()
    {
        var tenantEntity = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        var user = MfaEnabledUser(tenantEntity.Id);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenantEntity.Id, Arg.Any<CancellationToken>()).Returns(tenantEntity);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantEntity.Id);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var mailer = Substitute.For<IIdentityMailer>();

        var result = await Handler(users, tenants, tenant, currentUser, mailer)
            .Handle(new ResetUserMfaCommand(user.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.MfaEnabled.ShouldBeFalse();
        user.TotpSecretEncrypted.ShouldBeNull();
        await mailer.Received(1).SendMfaResetNoticeAsync(user.Email, user.FullName, tenantEntity.Name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SelfReset_ReturnsConflict_AndDoesNotSendNotice()
    {
        var tenantId = Guid.NewGuid();
        var user = MfaEnabledUser(tenantId);

        var users = Substitute.For<IUserRepository>();
        var tenants = Substitute.For<ITenantRepository>();
        var tenant = Substitute.For<ITenantContext>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(user.Id);
        var mailer = Substitute.For<IIdentityMailer>();

        var result = await Handler(users, tenants, tenant, currentUser, mailer)
            .Handle(new ResetUserMfaCommand(user.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.MfaSelfResetForbidden);
        await mailer.DidNotReceive().SendMfaResetNoticeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
