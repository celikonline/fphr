using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Auth;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>
/// Süper admin akışı: yeni şirket (kiracı) yalnız platform kiracısındaki IsPlatformStaff + SuperAdmin rollü kullanıcıyla açılır;
/// Owner/Admin dahil hiçbir müşteri rolü açamaz. Gerçek Identity yığını PostgreSQL üzerinde, tam Dispatcher hattıyla çalışır.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class CreateTenantTests : IdentityStackTestBase
{
    private const string StrongPassword = "Gunes-Deniz-2026";

    [Fact]
    public async Task SuperAdmin_CreatesTenantWithPassword_OwnerIsActiveAndCanSignIn()
    {
        var (platform, superAdmin) = await SeedPlatformAsync();
        ActAs(platform, superAdmin);

        var created = await ExecuteCommandAsync(NewTenant("acme", TenantOwnerMode.Password, StrongPassword));

        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
        created.Value.Slug.ShouldBe("acme");
        created.Value.InvitationSent.ShouldBeFalse();
        Mailer.Invitations.ShouldBeEmpty();

        var owner = await LoadUserAsync(created.Value.TenantId, "boss@acme.test");
        owner.Status.ShouldBe(UserStatus.Active);
        owner.IsPlatformStaff.ShouldBeFalse();

        var login = await LoginAnonymouslyAsync(new LoginCommand("boss@acme.test", StrongPassword, "acme", null, null));
        login.IsSuccess.ShouldBeTrue(login.IsFailure ? login.Error.Code : null);
        login.Value.Status.ShouldBe(LoginStatuses.Ok);
    }

    [Fact]
    public async Task SuperAdmin_CreatesTenantInInviteMode_OwnerStaysInvitedAndInvitationIsMailed()
    {
        var (platform, superAdmin) = await SeedPlatformAsync();
        ActAs(platform, superAdmin);

        var created = await ExecuteCommandAsync(NewTenant("globex", TenantOwnerMode.Invite, password: null));

        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Code : null);
        created.Value.InvitationSent.ShouldBeTrue();
        Mailer.Invitations.ShouldHaveSingleItem().To.ShouldBe("boss@acme.test");

        var owner = await LoadUserAsync(created.Value.TenantId, "boss@acme.test");
        owner.Status.ShouldBe(UserStatus.Invited);
        owner.PasswordHash.ShouldBeNull();
    }

    [Fact]
    public async Task NewTenant_GetsCustomerRolesOnly_AndItsOwnerHoldsNoPlatformPermission()
    {
        var (platform, superAdmin) = await SeedPlatformAsync();
        ActAs(platform, superAdmin);
        var created = (await ExecuteCommandAsync(NewTenant("initech", TenantOwnerMode.Password, StrongPassword))).Value;

        var db = Get<IdentityDbContext>();
        using (TenantContextSetter!.BeginScope(created.TenantId))
        {
            var codes = await db.Roles.Where(r => r.IsSystem).Select(r => r.Code).ToListAsync(Ct);
            codes.ShouldBe(SystemRoleCodes.All, ignoreOrder: true);
            codes.ShouldNotContain(SystemRoleCodes.SuperAdmin);
        }

        ActAs(created.TenantId, created.OwnerUserId);
        (await Get<IPermissionService>().HasAsync(created.OwnerUserId, IdentityPermissions.PlatformTenantsManage, Ct)).ShouldBeFalse();

        ActAs(platform, superAdmin);
        (await Get<IPermissionService>().HasAsync(superAdmin, IdentityPermissions.PlatformTenantsManage, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task CustomerTenantOwner_CannotCreateTenants()
    {
        await SeedPlatformAsync();
        var customer = await SeedTenantAsync("customer");
        var owner = await SeedUserAsync(customer, "owner@customer.test", SystemRoleCodes.Owner);
        var admin = await SeedUserAsync(customer, "admin@customer.test", SystemRoleCodes.HR);

        ActAs(customer, owner);
        ShouldFailWith(await ExecuteCommandAsync(NewTenant("sneaky", TenantOwnerMode.Password, StrongPassword)), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteQueryAsync(new ListTenantsQuery()), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(customer, admin);
        ShouldFailWith(await ExecuteCommandAsync(NewTenant("sneaky", TenantOwnerMode.Password, StrongPassword)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        (await Get<IdentityDbContext>().Tenants.AnyAsync(t => t.Slug == "sneaky", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task SuperAdminRoleWithoutPlatformStaffFlag_IsStillRefused()
    {
        var platform = await SeedTenantAsync(PlatformTenant.Slug);
        var notStaff = await SeedUserAsync(platform, "almost@sense.test", SystemRoleCodes.SuperAdmin, platformStaff: false);

        ActAs(platform, notStaff);

        ShouldFailWith(await ExecuteCommandAsync(NewTenant("acme", TenantOwnerMode.Password, StrongPassword)), IdentityErrors.PlatformStaffRequired, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteQueryAsync(new ListTenantsQuery()), IdentityErrors.PlatformStaffRequired, ErrorType.Forbidden);
    }

    [Fact]
    public async Task DuplicateAndReservedSlugs_AreRejected()
    {
        var (platform, superAdmin) = await SeedPlatformAsync();
        ActAs(platform, superAdmin);

        (await ExecuteCommandAsync(NewTenant("acme", TenantOwnerMode.Password, StrongPassword))).IsSuccess.ShouldBeTrue();

        ShouldFailWith(await ExecuteCommandAsync(NewTenant("acme", TenantOwnerMode.Password, StrongPassword)), IdentityErrors.TenantSlugTaken, ErrorType.Conflict);
        ShouldFailWith(await ExecuteCommandAsync(NewTenant(PlatformTenant.Slug, TenantOwnerMode.Password, StrongPassword)), IdentityErrors.TenantSlugTaken, ErrorType.Conflict);
    }

    [Fact]
    public async Task ListTenants_ReturnsEveryTenant_NewestFirst_AndFlagsThePlatformTenant()
    {
        var (platform, superAdmin) = await SeedPlatformAsync();
        ActAs(platform, superAdmin);

        // Denetim alanları test saatinden dolar: kayıtların CreatedAt değeri ayrışsın diye saat ilerletilir.
        Time.Advance(TimeSpan.FromMinutes(5));
        await ExecuteCommandAsync(NewTenant("acme", TenantOwnerMode.Password, StrongPassword));

        var list = (await ExecuteQueryAsync(new ListTenantsQuery())).Value;

        list.Select(t => t.Slug).ShouldBe(["acme", PlatformTenant.Slug]);
        list.Single(t => t.Slug == PlatformTenant.Slug).IsPlatform.ShouldBeTrue();
        list.Single(t => t.Slug == "acme").IsPlatform.ShouldBeFalse();
    }

    [Fact]
    public async Task SuperAdmin_UpdatesAnotherTenantsProfile_AndSlugStaysUntouched()
    {
        var (platform, superAdmin) = await SeedPlatformAsync();
        ActAs(platform, superAdmin);
        var created = (await ExecuteCommandAsync(NewTenant("acme", TenantOwnerMode.Password, StrongPassword))).Value;

        var updated = await ExecuteCommandAsync(new UpdateTenantByPlatformCommand(created.TenantId, "Acme Yazılım A.Ş.", "1234567890", "en-US", "Europe/London"));

        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
        updated.Value.Name.ShouldBe("Acme Yazılım A.Ş.");
        updated.Value.TaxNumber.ShouldBe("1234567890");
        updated.Value.Slug.ShouldBe("acme");

        var listed = (await ExecuteQueryAsync(new ListTenantsQuery())).Value.Single(t => t.Slug == "acme");
        listed.Name.ShouldBe("Acme Yazılım A.Ş.");
        listed.Locale.ShouldBe("en-US");
        listed.TimeZone.ShouldBe("Europe/London");
    }

    [Fact]
    public async Task CustomerTenantOwner_CannotUpdateTenantProfilesThroughPlatformEndpoint()
    {
        await SeedPlatformAsync();
        var customer = await SeedTenantAsync("customer");
        var owner = await SeedUserAsync(customer, "owner@customer.test", SystemRoleCodes.Owner);

        ActAs(customer, owner);

        ShouldFailWith(await ExecuteCommandAsync(new UpdateTenantByPlatformCommand(customer, "Hacked", null, "tr-TR", "Europe/Istanbul")), ErrorCodes.Forbidden, ErrorType.Forbidden);
    }

    private async Task<(Guid Platform, Guid SuperAdmin)> SeedPlatformAsync()
    {
        var platform = await SeedTenantAsync(PlatformTenant.Slug);
        var superAdmin = await SeedUserAsync(platform, "root@sense.test", SystemRoleCodes.SuperAdmin, platformStaff: true);
        return (platform, superAdmin);
    }

    /// <summary>Giriş gerçekte anonim çalışır (kiracı/kullanıcı bağlamı yok); önceki <c>ActAs</c> bağlamı bu çağrıya akmasın.</summary>
    private Task<Result<LoginResultDto>> LoginAnonymouslyAsync(LoginCommand command)
    {
        using var suppressed = ExecutionContext.SuppressFlow();
        return Task.Run(() => ExecuteCommandAsync(command));
    }

    private static CreateTenantCommand NewTenant(string slug, TenantOwnerMode mode, string? password) =>
        new("Acme A.Ş.", slug, "boss@acme.test", "Ali", "Veli", mode, password, null, "tr");

    private async Task<User> LoadUserAsync(Guid tenantId, string email)
    {
        var db = Get<IdentityDbContext>();
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            return await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct);
        }
    }
}
