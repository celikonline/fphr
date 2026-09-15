using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Impersonation;
using Sense.Modules.Identity.Domain.Invitations;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Security;

public sealed class IdentityFaz0DomainTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ImpersonationGrant_TracksApprovalTime_AndSessionActivityHonoursExpiryAndRevoke()
    {
        var grant = ImpersonationGrant.Create(Guid.NewGuid(), Guid.NewGuid(), "ticket", Now).Value;
        grant.IsSessionActive(Now).ShouldBeFalse();

        grant.Approve(Guid.NewGuid(), Now.AddMinutes(5), TimeSpan.FromHours(1)).IsSuccess.ShouldBeTrue();
        grant.ApprovedAt.ShouldBe(Now.AddMinutes(5));
        grant.IsSessionActive(Now.AddMinutes(10)).ShouldBeFalse();

        grant.Start(Now.AddMinutes(10)).IsSuccess.ShouldBeTrue();
        grant.IsSessionActive(Now.AddMinutes(30)).ShouldBeTrue();
        grant.IsSessionActive(Now.AddHours(2)).ShouldBeFalse();
        grant.EffectiveStatus(Now.AddMinutes(30)).ShouldBe(ImpersonationGrantStatus.Active);
        grant.EffectiveStatus(Now.AddHours(2)).ShouldBe(ImpersonationGrantStatus.Expired);

        grant.Revoke(Now.AddMinutes(40)).IsSuccess.ShouldBeTrue();
        grant.IsSessionActive(Now.AddMinutes(41)).ShouldBeFalse();
        grant.EffectiveStatus(Now.AddMinutes(41)).ShouldBe(ImpersonationGrantStatus.Revoked);
    }

    [Fact]
    public void Invitation_RenewCanRetargetAddressSourceAndBatch()
    {
        var invitation = Invitation.Create(Guid.NewGuid(), Guid.NewGuid(), "Old@Acme.test", "hash-1", Now, TimeSpan.FromHours(72), null, InvitationEmailSource.Personal);
        invitation.EmailSource.ShouldBe(InvitationEmailSource.Personal);
        invitation.BatchId.ShouldBeNull();

        var batch = Guid.NewGuid();
        invitation.Renew("hash-2", Now.AddDays(1), TimeSpan.FromHours(72), "new@acme.test", InvitationEmailSource.Work, batch).IsSuccess.ShouldBeTrue();

        invitation.Email.ShouldBe("new@acme.test");
        invitation.EmailSource.ShouldBe(InvitationEmailSource.Work);
        invitation.BatchId.ShouldBe(batch);
        invitation.SendCount.ShouldBe(2);
        invitation.ExpiresAt.ShouldBe(Now.AddDays(1).AddHours(72));
    }

    [Fact]
    public void RoleSync_AppliesOnlyDifferences_AndReportsWhetherAnythingChanged()
    {
        var role = Role.CreateSystem(Guid.NewGuid(), SystemRoleCodes.Employee);
        role.ReplacePermissionsUnchecked([new RolePermission(role.Id, "a.b.view", 0), new RolePermission(role.Id, "stale.b.view", 0), new RolePermission(role.Id, "c.d.view", 0)]);
        var kept = role.Permissions.Single(p => p.Permission == "a.b.view");

        var changed = role.SyncPermissionsUnchecked([new RolePermission(role.Id, "a.b.view", 0), new RolePermission(role.Id, "c.d.view", 4), new RolePermission(role.Id, "new.b.view", 0)]);

        changed.ShouldBeTrue();
        role.Permissions.Select(p => (p.Permission, p.Scope)).ShouldBe([("a.b.view", 0), ("c.d.view", 4), ("new.b.view", 0)], ignoreOrder: true);
        role.Permissions.Single(p => p.Permission == "a.b.view").ShouldBeSameAs(kept);
        role.SyncPermissionsUnchecked(role.Permissions.Select(p => new RolePermission(role.Id, p.Permission, p.Scope)).ToList()).ShouldBeFalse();
    }

    [Fact]
    public void AccessSettingsBypass_GoesToOwnerAdminAndHr_ButNotToEmployeeOrItAdmin()
    {
        IReadOnlyList<Permission> catalog =
            [new(AccessSettingPermissions.Bypass, "identity", "x", "g"), new("identity.users.view", "identity", "x", "g"), new("employee.my.view", "employee", "x", "g")];

        bool Has(string code) => SystemRoleDefinitions.Get(code).Resolve(Guid.NewGuid(), catalog).Any(p => p.Permission == AccessSettingPermissions.Bypass);

        Has(SystemRoleCodes.Owner).ShouldBeTrue();
        Has(SystemRoleCodes.Admin).ShouldBeTrue();
        Has(SystemRoleCodes.HR).ShouldBeTrue();
        Has(SystemRoleCodes.Employee).ShouldBeFalse();
        Has(SystemRoleCodes.Intern).ShouldBeFalse();
        Has(SystemRoleCodes.Manager).ShouldBeFalse();
        Has(SystemRoleCodes.ITAdmin).ShouldBeFalse();
        Has(SystemRoleCodes.Auditor).ShouldBeFalse();

        SystemRoleDefinitions.Get(SystemRoleCodes.Employee).Resolve(Guid.NewGuid(), catalog).Select(p => p.Permission).ShouldContain("employee.my.view");
    }
}
