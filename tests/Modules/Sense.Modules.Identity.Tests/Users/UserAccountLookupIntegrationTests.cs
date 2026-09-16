using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>IUserAccountLookup against real PostgreSQL: status mapping, access type precedence, last invitation time.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class UserAccountLookupIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName);
    }

    [Fact]
    public async Task GetAsync_MapsStatusAccessTypeAndLastInvitation()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = ServiceProvider!.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(ct);

        var tenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        var owner = Role.CreateSystem(tenantId, SystemRoleCodes.Owner);
        var admin = Role.CreateSystem(tenantId, SystemRoleCodes.Admin);
        var employee = Role.CreateSystem(tenantId, SystemRoleCodes.Employee);
        var hr = Role.CreateSystem(tenantId, SystemRoleCodes.HR);
        db.Roles.AddRange(owner, admin, employee, hr);

        var ownerUser = NewUser(tenantId, "owner@acme.com", employee.Id, owner.Id);
        ownerUser.SetPassword("hash");
        var adminUser = NewUser(tenantId, "admin@acme.com", employee.Id, admin.Id);
        adminUser.Disable();
        var invitedUser = NewUser(tenantId, "invited@acme.com", employee.Id);
        var hrOnlyUser = NewUser(tenantId, "hr@acme.com", hr.Id);
        db.Users.AddRange(ownerUser, adminUser, invitedUser, hrOnlyUser);

        var invitation = Invitation.Create(tenantId, invitedUser.Id, invitedUser.Email, "token-hash", DateTime.UtcNow, TimeSpan.FromDays(7), ownerUser.Id);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var lookup = new UserAccountLookup(db);
        var unknown = Guid.NewGuid();
        var result = await lookup.GetAsync(tenantId, [ownerUser.Id, adminUser.Id, invitedUser.Id, hrOnlyUser.Id, unknown], ct);

        result.ContainsKey(unknown).ShouldBeFalse();
        result[ownerUser.Id].ShouldBe(new UserAccountInfo(ownerUser.Id, UserAccountStatus.Active, SystemRoleCodes.Owner, null));
        result[adminUser.Id].Status.ShouldBe(UserAccountStatus.Disabled);
        result[adminUser.Id].AccessType.ShouldBe(SystemRoleCodes.Admin);
        result[invitedUser.Id].Status.ShouldBe(UserAccountStatus.Invited);
        result[invitedUser.Id].AccessType.ShouldBe(SystemRoleCodes.Employee);
        result[invitedUser.Id].LastInvitationSentAt.ShouldNotBeNull();
        result[hrOnlyUser.Id].AccessType.ShouldBeNull();

        (await lookup.GetAsync(Guid.NewGuid(), [ownerUser.Id], ct)).ShouldBeEmpty();
    }

    private static User NewUser(Guid tenantId, string email, params Guid[] roleIds)
    {
        var user = User.Create(tenantId, email, "Test", "User", "tr").Value;
        user.SetRoles(roleIds);
        return user;
    }
}
