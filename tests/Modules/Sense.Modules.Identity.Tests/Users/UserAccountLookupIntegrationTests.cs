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
        var hr = Role.CreateSystem(tenantId, SystemRoleCodes.HR);
        var payroll = Role.CreateSystem(tenantId, SystemRoleCodes.PayrollSpecialist);
        var employee = Role.CreateSystem(tenantId, SystemRoleCodes.Employee);
        // Henüz senkronize edilmemiş eski bir sistem rolü (Admin) erişim türü sayılmaz.
        var retiredAdmin = Role.CreateSystem(tenantId, SystemRoleCodes.Admin);
        db.Roles.AddRange(owner, hr, payroll, employee, retiredAdmin);

        // Owner + İK Yöneticisi → "HR" (Owner bir grup değil, İK Yöneticisiyle taşınan bayrak).
        var ownerUser = NewUser(tenantId, "owner@acme.com", owner.Id, hr.Id);
        ownerUser.SetPassword("hash");
        // Muhasebe + Çalışan → öncelik sırası HR, PayrollSpecialist, Employee.
        var payrollUser = NewUser(tenantId, "payroll@acme.com", employee.Id, payroll.Id);
        payrollUser.Disable();
        var invitedUser = NewUser(tenantId, "invited@acme.com", employee.Id);
        var ownerOnlyUser = NewUser(tenantId, "owner-only@acme.com", owner.Id);
        var legacyUser = NewUser(tenantId, "legacy@acme.com", retiredAdmin.Id);
        db.Users.AddRange(ownerUser, payrollUser, invitedUser, ownerOnlyUser, legacyUser);

        var invitation = Invitation.Create(tenantId, invitedUser.Id, invitedUser.Email, "token-hash", DateTime.UtcNow, TimeSpan.FromDays(7), ownerUser.Id);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var lookup = new UserAccountLookup(db);
        var unknown = Guid.NewGuid();
        var result = await lookup.GetAsync(tenantId, [ownerUser.Id, payrollUser.Id, invitedUser.Id, ownerOnlyUser.Id, legacyUser.Id, unknown], ct);

        result.ContainsKey(unknown).ShouldBeFalse();
        result[ownerUser.Id].ShouldBe(new UserAccountInfo(ownerUser.Id, UserAccountStatus.Active, SystemRoleCodes.HR, null));
        result[payrollUser.Id].Status.ShouldBe(UserAccountStatus.Disabled);
        result[payrollUser.Id].AccessType.ShouldBe(SystemRoleCodes.PayrollSpecialist);
        result[invitedUser.Id].Status.ShouldBe(UserAccountStatus.Invited);
        result[invitedUser.Id].AccessType.ShouldBe(SystemRoleCodes.Employee);
        result[invitedUser.Id].LastInvitationSentAt.ShouldNotBeNull();
        result[ownerOnlyUser.Id].AccessType.ShouldBeNull();
        result[legacyUser.Id].AccessType.ShouldBeNull();

        (await lookup.GetAsync(Guid.NewGuid(), [ownerUser.Id], ct)).ShouldBeEmpty();
    }

    private static User NewUser(Guid tenantId, string email, params Guid[] roleIds)
    {
        var user = User.Create(tenantId, email, "Test", "User", "tr").Value;
        user.SetRoles(roleIds);
        return user;
    }
}
