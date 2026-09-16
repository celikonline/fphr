using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Tokens;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Auth;

/// <summary>
/// G-3-11 (gün açılışı TALIMAT'ı, "tur içi ek iş" 18:22): fesihle (<see cref="Sense.Modules.Identity.Application.Users.EmployeeTerminatedHandler"/>
/// aracılığıyla veya doğrudan) <see cref="User.Disable"/> edilen kullanıcının hâlâ aktif (süresi dolmamış, iptal
/// edilmemiş) bir refresh token'ı olsa bile <see cref="RefreshTokenHandler"/> bunu <see cref="User.CanSignIn"/> ile
/// reddeder - yeni token çifti üretilmez, mevcut token rotate edilmez.
/// </summary>
public sealed class RefreshTokenHandlerTests
{
    private static RefreshTokenHandler Handler(
        IRefreshTokenRepository refreshTokens,
        IUserRepository users,
        ITenantRepository tenants,
        IRoleRepository roles,
        ISecretGenerator secrets,
        ITokenService tokens,
        ITenantContextSetter tenantSetter) =>
        new(refreshTokens, users, tenants, roles, secrets, tokens, tenantSetter, TimeProvider.System);

    [Fact]
    public async Task Handle_DisabledUser_RejectsRefresh_NoNewTokenIssued()
    {
        var now = DateTime.UtcNow;
        var tenant = Tenant.Create("acme", "Acme", now, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
        var user = User.Create(tenant.Id, "eren@example.com", "Eren", "Yılmaz", "tr").Value;
        user.Disable();

        var refreshToken = RefreshToken.Issue(tenant.Id, user.Id, null, "hash", now, TimeSpan.FromDays(30), null, null);

        var refreshTokens = Substitute.For<IRefreshTokenRepository>();
        refreshTokens.GetByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(refreshToken);

        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var roles = Substitute.For<IRoleRepository>();
        var secrets = Substitute.For<ISecretGenerator>();
        secrets.Hash(Arg.Any<string>()).Returns("hash");
        var tokens = Substitute.For<ITokenService>();
        var tenantSetter = Substitute.For<ITenantContextSetter>();
        tenantSetter.BeginScope(Arg.Any<Guid>(), Arg.Any<string?>()).Returns(Substitute.For<IDisposable>());

        var handler = Handler(refreshTokens, users, tenants, roles, secrets, tokens, tenantSetter);

        var result = await handler.Handle(new RefreshTokenCommand("raw-token", null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityDomainErrors.UserDisabled);
        refreshTokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
        tokens.DidNotReceive().IssueAccessToken(
            Arg.Any<User>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<DateTime?>());
    }
}
