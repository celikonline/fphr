using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Publishing;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>Faz 1 tur 1: permission-based recipient resolution (S-3 sözleşme değişikliği yok, yalnız ekleme).</summary>
public sealed class NotificationRecipientResolverTests
{
    private const string PermittedPermissionKey = "documents.requirements.view";
    private const string UnmappedPermissionKey = "documents.some.unmapped.key";

    [Fact]
    public async Task ResolveByPermissionAsync_ReturnsEmployeeIds_OfRolesHoldingTheMappedPermission()
    {
        var tenantId = Guid.NewGuid();
        var hrEmployeeId = Guid.NewGuid();
        var managerEmployeeId = Guid.NewGuid();
        var byRole = new Dictionary<string, Guid[]>(StringComparer.Ordinal) { ["HR"] = [hrEmployeeId], ["Manager"] = [managerEmployeeId] };
        var roleAssignments = Substitute.For<IRoleAssignmentLookup>();
        roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => byRole.GetValueOrDefault(call.ArgAt<string>(1), []));
        var resolver = CreateResolver(roleAssignments);

        var result = await resolver.ResolveByPermissionAsync(tenantId, PermittedPermissionKey, CancellationToken.None);

        result.ShouldBe([hrEmployeeId, managerEmployeeId], ignoreOrder: true);
    }

    [Fact]
    public async Task ResolveByPermissionAsync_UnmappedPermission_FallsBackToOwnerAdminOnly_NoOtherRoleQueried()
    {
        var tenantId = Guid.NewGuid();
        var roleAssignments = Substitute.For<IRoleAssignmentLookup>();
        roleAssignments.ListEmployeeIdsInRoleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        var resolver = CreateResolver(roleAssignments);

        var result = await resolver.ResolveByPermissionAsync(tenantId, UnmappedPermissionKey, CancellationToken.None);

        result.ShouldBeEmpty();
        await roleAssignments.Received(1).ListEmployeeIdsInRoleAsync(tenantId, "Owner", Arg.Any<CancellationToken>());
        await roleAssignments.Received(1).ListEmployeeIdsInRoleAsync(tenantId, "Admin", Arg.Any<CancellationToken>());
        await roleAssignments.DidNotReceive().ListEmployeeIdsInRoleAsync(tenantId, "HR", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveByPermissionAsync_DoesNotLeakEmployeeIdsAcrossTenants()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var ownEmployeeId = Guid.NewGuid();
        var otherTenantEmployeeId = Guid.NewGuid();
        var roleAssignments = Substitute.For<IRoleAssignmentLookup>();
        roleAssignments.ListEmployeeIdsInRoleAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<string>(1) == "HR" ? [ownEmployeeId] : Array.Empty<Guid>());
        roleAssignments.ListEmployeeIdsInRoleAsync(otherTenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([otherTenantEmployeeId]);
        var resolver = CreateResolver(roleAssignments);

        var result = await resolver.ResolveByPermissionAsync(tenantId, PermittedPermissionKey, CancellationToken.None);

        result.ShouldBe([ownEmployeeId]);
        result.ShouldNotContain(otherTenantEmployeeId);
        await roleAssignments.DidNotReceive().ListEmployeeIdsInRoleAsync(otherTenantId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static NotificationRecipientResolver CreateResolver(IRoleAssignmentLookup roleAssignments)
    {
        var contacts = Substitute.For<IEmployeeContactLookup>();
        var userProfiles = Substitute.For<IUserProfileLookup>();
        var options = Options.Create(new NotificationsOptions { DefaultLanguage = "tr" });
        return new NotificationRecipientResolver(contacts, userProfiles, roleAssignments, options);
    }
}
