using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Regresyon: başarısız kimlik doğrulamada UnitOfWorkBehaviour SaveChanges yapmaz; sayaç/kilit veritabanına gerçekten
/// yazılmalıdır. Test fixture'ı tek DbContext (change tracker) paylaştığı için, kalıcılık her adımdan sonra
/// <c>ChangeTracker.Clear()</c> ile izlenen durum atılıp veritabanından yeniden okunarak doğrulanır.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class LoginLockoutPersistenceTests : IdentityStackTestBase
{
    private async Task<(int Failed, DateTime? LockoutEnd)> ReadPersistedAsync(Guid userId)
    {
        var db = Get<IdentityDbContext>();
        db.ChangeTracker.Clear();
        var row = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .Select(u => new { u.FailedAccessCount, u.LockoutEndUtc }).SingleAsync(Ct);
        return (row.FailedAccessCount, row.LockoutEndUtc);
    }

    [Fact]
    public async Task WrongPasswords_ArePersisted_LockAccount_AndCorrectPasswordIsRejectedWhileLocked()
    {
        var tenantId = await SeedTenantAsync("lockout-persist");
        var email = "kilit@acme.test";
        var userId = await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);

        ShouldFailWith(
            await ExecuteCommandAsync(new LoginCommand(email, "wrong-password", "lockout-persist", null, null)),
            IdentityErrors.InvalidCredentials, ErrorType.Unauthorized);
        (await ReadPersistedAsync(userId)).Failed.ShouldBe(1);

        for (var i = 1; i < IdentityDefaults.MaxFailedAccessAttempts; i++)
        {
            ShouldFailWith(
                await ExecuteCommandAsync(new LoginCommand(email, "wrong-password", "lockout-persist", null, null)),
                IdentityErrors.InvalidCredentials, ErrorType.Unauthorized);
        }

        (await ReadPersistedAsync(userId)).LockoutEnd.ShouldNotBeNull();

        ShouldFailWith(
            await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "lockout-persist", null, null)),
            IdentityDomainErrors.LockedOut, ErrorType.Unauthorized);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsPersistedFailedCount()
    {
        var tenantId = await SeedTenantAsync("lockout-reset");
        var email = "sifirla@acme.test";
        var userId = await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);

        await ExecuteCommandAsync(new LoginCommand(email, "wrong-password", "lockout-reset", null, null));
        (await ReadPersistedAsync(userId)).Failed.ShouldBe(1);

        (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "lockout-reset", null, null))).IsSuccess.ShouldBeTrue();
        (await ReadPersistedAsync(userId)).Failed.ShouldBe(0);
    }

    [Fact]
    public async Task RefreshTokenReuse_RevokesWholeFamily_Persisted()
    {
        var tenantId = await SeedTenantAsync("refresh-reuse");
        var email = "yenile@acme.test";
        await SeedUserAsync(tenantId, email, SystemRoleCodes.Employee);

        var login = (await ExecuteCommandAsync(new LoginCommand(email, "Password-12345", "refresh-reuse", null, null))).Value;
        var first = login.Tokens!.RefreshToken;

        var second = (await ExecuteCommandAsync(new RefreshTokenCommand(first, null, null))).Value;

        // İptal edilmiş ilk token yeniden kullanılır → aile kapanır (kalıcı olarak).
        ShouldFailWith(await ExecuteCommandAsync(new RefreshTokenCommand(first, null, null)), IdentityErrors.RefreshTokenReused, ErrorType.Unauthorized);

        Get<IdentityDbContext>().ChangeTracker.Clear();
        ShouldFailWith(await ExecuteCommandAsync(new RefreshTokenCommand(second.RefreshToken, null, null)), IdentityErrors.RefreshTokenReused, ErrorType.Unauthorized);
    }
}
