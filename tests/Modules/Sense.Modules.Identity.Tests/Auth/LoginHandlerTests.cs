using Microsoft.Extensions.Options;
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
/// Hesap kilidi regresyonu: handler başarısız (Error) döndüğünde <c>UnitOfWorkBehaviour</c> SaveChanges yapmaz, bu yüzden
/// yanlış parola sayacı / kilit handler içinde açıkça kalıcılaştırılmalıdır. Ayrıca durum hataları (davetli/kilitli/pasif)
/// yalnızca parola doğruysa döner (kullanıcı numaralandırma).
/// </summary>
public sealed class LoginHandlerTests
{
    private const string GoodPassword = "Correct-Password-1";
    private const string Hash = "stored-hash";

    private readonly IIdentityUnitOfWork _uow = Substitute.For<IIdentityUnitOfWork>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly Tenant _tenant = Tenant.Create("acme", "Acme", DateTime.UtcNow, trialDays: 0, "tr-TR", "Europe/Istanbul").Value;
    private readonly IdentityOptions _identityOptions = new();

    public LoginHandlerTests() =>
        _hasher.Verify(Hash, Arg.Any<string>()).Returns(ci => (string)ci[1] == GoodPassword);

    private User ActiveUser()
    {
        var user = User.Create(_tenant.Id, "eren@example.com", "Eren", "Yılmaz", "tr").Value;
        user.SetPassword(Hash);
        return user;
    }

    private LoginHandler Handler(User user)
    {
        var users = Substitute.For<IUserRepository>();
        users.FindAcrossTenantsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<(User User, Guid TenantId)>)new[] { (user, _tenant.Id) });
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);
        tenants.GetBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(_tenant);
        var setter = Substitute.For<ITenantContextSetter>();
        setter.BeginScope(Arg.Any<Guid>(), Arg.Any<string?>()).Returns(Substitute.For<IDisposable>());
        var tokens = Substitute.For<ITokenService>();
        tokens.RefreshTokenLifetime.Returns(TimeSpan.FromDays(30));
        tokens.IssueAccessToken(
            Arg.Any<User>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<DateTime?>())
            .Returns(new AccessToken("jwt", DateTime.UtcNow.AddMinutes(15)));
        var secrets = Substitute.For<ISecretGenerator>();
        secrets.NewToken(Arg.Any<int>()).Returns("raw");
        secrets.Hash(Arg.Any<string>()).Returns("h");
        var roles = Substitute.For<IRoleRepository>();
        roles.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<Sense.Modules.Identity.Domain.Roles.Role>());

        return new LoginHandler(
            users, tenants, roles, Substitute.For<IRefreshTokenRepository>(), _hasher, secrets, tokens, setter,
            Substitute.For<IMfaChallengeTokenProtector>(), Options.Create(_identityOptions), _uow, TimeProvider.System);
    }

    private static LoginCommand Command(string password) => new("eren@example.com", password, "acme", null, null);

    [Fact]
    public async Task WrongPassword_ReturnsInvalidCredentials_AndPersistsFailedAccessCount()
    {
        var user = ActiveUser();

        var result = await Handler(user).Handle(Command("wrong"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.InvalidCredentials);
        user.FailedAccessCount.ShouldBe(1);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatedWrongPasswords_LockAccount_AndPersistEveryAttempt()
    {
        var user = ActiveUser();
        var handler = Handler(user);

        for (var i = 0; i < _identityOptions.MaxFailedAccessAttempts; i++)
        {
            (await handler.Handle(Command("wrong"), CancellationToken.None)).Error.Code.ShouldBe(IdentityErrors.InvalidCredentials);
        }

        user.LockoutEndUtc.ShouldNotBeNull();
        await _uow.Received(_identityOptions.MaxFailedAccessAttempts).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Kilitliyken doğru parola da reddedilir.
        var locked = await handler.Handle(Command(GoodPassword), CancellationToken.None);
        locked.IsFailure.ShouldBeTrue();
        locked.Error.Code.ShouldBe(IdentityDomainErrors.LockedOut);
    }

    [Fact]
    public async Task WrongPassword_WhileLocked_DoesNotRevealLockAndDoesNotCount()
    {
        var user = ActiveUser();
        user.RecordFailedAccess(DateTime.UtcNow, new LockoutPolicy(1, TimeSpan.FromMinutes(15)));
        _uow.ClearReceivedCalls();

        var result = await Handler(user).Handle(Command("wrong"), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.InvalidCredentials);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvitedAccount_WrongPassword_ReturnsGenericInvalidCredentials()
    {
        var user = User.Create(_tenant.Id, "eren@example.com", "Eren", "Yılmaz", "tr").Value;

        var result = await Handler(user).Handle(Command("whatever"), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.InvalidCredentials);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisabledAccount_WrongPassword_ReturnsGeneric_CorrectPassword_ReturnsDisabled()
    {
        var user = ActiveUser();
        user.Disable();
        var handler = Handler(user);

        (await handler.Handle(Command("wrong"), CancellationToken.None)).Error.Code.ShouldBe(IdentityErrors.InvalidCredentials);
        (await handler.Handle(Command(GoodPassword), CancellationToken.None)).Error.Code.ShouldBe(IdentityDomainErrors.UserDisabled);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsFailedAccessCount()
    {
        var user = ActiveUser();
        var handler = Handler(user);
        await handler.Handle(Command("wrong"), CancellationToken.None);
        user.FailedAccessCount.ShouldBe(1);

        var result = await handler.Handle(Command(GoodPassword), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.FailedAccessCount.ShouldBe(0);
    }
}
