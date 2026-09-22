using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Süper admin hesabı yapılandırmadan (Platform:SuperAdmin) API açılışında idempotent oluşturulur: "platform" kiracısı,
/// SuperAdmin rolü ve IsPlatformStaff kullanıcı. Oluşan hesap gerçekten yeni şirket açabilmelidir (uçtan uca).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PlatformBootstrapperTests : IdentityStackTestBase
{
    private const string RootEmail = "root@sense.test";
    private const string RootPassword = "Platform-Root-2026!";

    [Fact]
    public async Task Ensure_WithoutConfiguration_DoesNothing()
    {
        (await NewBootstrapper().EnsureAsync(Ct)).ShouldBeFalse();

        (await Get<IdentityDbContext>().Tenants.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Ensure_CreatesPlatformTenantSuperAdminRoleAndStaffUser_ThatCanCreateTenants()
    {
        Configure(RootEmail, RootPassword);

        (await NewBootstrapper().EnsureAsync(Ct)).ShouldBeTrue();

        var db = Get<IdentityDbContext>();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(Ct);
        tenant.Slug.ShouldBe(PlatformTenant.Slug);
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.IsSetupCompleted.ShouldBeTrue();

        Guid userId;
        using (TenantContextSetter!.BeginScope(tenant.Id))
        {
            var roleCodes = await db.Roles.Where(r => r.IsSystem).Select(r => r.Code).ToListAsync(Ct);
            roleCodes.ShouldContain(SystemRoleCodes.SuperAdmin);

            var user = await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == RootEmail.ToUpperInvariant(), Ct);
            user.IsPlatformStaff.ShouldBeTrue();
            user.Status.ShouldBe(UserStatus.Active);
            userId = user.Id;
        }

        ActAs(tenant.Id, userId);
        (await Get<IPermissionService>().HasAsync(userId, IdentityPermissions.PlatformTenantsManage, Ct)).ShouldBeTrue();

        var created = await ExecuteCommandAsync(
            new CreateTenantCommand("Acme A.Ş.", "acme", "boss@acme.test", "Ali", "Veli", TenantOwnerMode.Invite, null, null, "tr"));
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
    }

    [Fact]
    public async Task Ensure_IsIdempotent_AndKeepsTheExistingPassword()
    {
        Configure(RootEmail, RootPassword);
        (await NewBootstrapper().EnsureAsync(Ct)).ShouldBeTrue();

        var db = Get<IdentityDbContext>();
        var tenantId = await db.Tenants.Select(t => t.Id).SingleAsync(Ct);
        string firstHash;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            firstHash = (await db.Users.AsNoTracking().SingleAsync(Ct)).PasswordHash!;
        }

        Configure(RootEmail, "Baska-Bir-Parola-2027!");
        (await NewBootstrapper().EnsureAsync(Ct)).ShouldBeTrue();

        (await db.Tenants.CountAsync(Ct)).ShouldBe(1);
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var user = await db.Users.AsNoTracking().SingleAsync(Ct);
            user.PasswordHash.ShouldBe(firstHash);
            user.IsPlatformStaff.ShouldBeTrue();
            (await db.Roles.CountAsync(r => r.Code == SystemRoleCodes.SuperAdmin, Ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Ensure_WithWeakPassword_IsSkipped()
    {
        Configure(RootEmail, "weak");

        (await NewBootstrapper().EnsureAsync(Ct)).ShouldBeFalse();

        (await Get<IdentityDbContext>().Tenants.AnyAsync(Ct)).ShouldBeFalse();
    }

    private void Configure(string email, string password)
    {
        var options = Get<IOptions<PlatformOptions>>().Value;
        options.Email = email;
        options.Password = password;
    }

    private PlatformBootstrapper NewBootstrapper() => Get<PlatformBootstrapper>();
}
