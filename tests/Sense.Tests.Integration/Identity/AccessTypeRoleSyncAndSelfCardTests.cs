using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Identity.Infrastructure.Security;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration.Identity;

/// <summary>Faz 0 item 2 (access type rules), system role permission sync for existing tenants, and the employee self card.</summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class AccessTypeRoleSyncAndSelfCardTests : IdentityStackTestBase
{
    [Fact]
    public async Task AccessType_OnlyOwnerTouchesOwner_AdminManagesAdminAndEmployee_HrNeither_LastOwnerKept()
    {
        var tenantId = await SeedTenantAsync("access-type");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var adminId = await SeedUserAsync(tenantId, "admin@acme.test", SystemRoleCodes.Admin);
        var hrId = await SeedUserAsync(tenantId, "hr@acme.test", SystemRoleCodes.HR);
        var targetId = await SeedUserAsync(tenantId, "target@acme.test", SystemRoleCodes.Employee);

        // Owner alone: cannot demote themselves.
        ActAs(tenantId, ownerId);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.Admin)), IdentityErrors.UserLastOwner, ErrorType.Rule);

        ActAs(tenantId, adminId);
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Admin))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Employee))).IsSuccess.ShouldBeTrue();
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Owner)), IdentityErrors.AccessTypeOwnerOnly, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.Employee)), IdentityErrors.AccessTypeOwnerOnly, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserRolesCommand(targetId, [await RoleIdAsync(tenantId, SystemRoleCodes.Owner)])),
            IdentityErrors.AccessTypeOwnerOnly, ErrorType.Forbidden);

        // senseik-050: HR Manager no longer carries identity.users.manage at all (identity management is
        // System Administrator-only now), so every one of these is now rejected by AuthorizationBehaviour
        // (generic ErrorCodes.Forbidden) before the request ever reaches AccessTypeGuard's Owner/Admin-specific
        // domain rule (IdentityErrors.AccessTypeForbidden) - including the Employee-to-Employee no-op that used
        // to succeed for HR.
        ActAs(tenantId, hrId);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Admin)), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserRolesCommand(adminId, [await RoleIdAsync(tenantId, SystemRoleCodes.HR)])),
            ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new CreateUserCommand("new@acme.test", "Yeni", "Yönetici", null, [await RoleIdAsync(tenantId, SystemRoleCodes.Admin)], null, false, null)),
            ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Employee)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(tenantId, ownerId);
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Owner))).IsSuccess.ShouldBeTrue();
        (await AccessTypeOfAsync(tenantId, targetId)).ShouldBe(SystemRoleCodes.Owner);

        // With a second active owner the first may step down; the remaining one is then the last owner again.
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.Admin))).IsSuccess.ShouldBeTrue();
        ActAs(tenantId, targetId);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Employee)), IdentityErrors.UserLastOwner, ErrorType.Rule);
    }

    [Fact]
    public async Task SystemRoleSync_AddsMissingAndRemovesStalePermissions_OnlyOnSystemRoles_AndIsIdempotent()
    {
        var tenantId = await SeedTenantAsync("role-sync");
        var employeeUserId = await SeedUserAsync(tenantId, "plain@acme.test", SystemRoleCodes.Employee);
        var db = Get<IdentityDbContext>();

        const string Stale = "legacy.removed.view";
        Guid customRoleId;
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var employeeRole = await db.Roles.SingleAsync(r => r.Code == SystemRoleCodes.Employee, Ct);
            employeeRole.ReplacePermissionsUnchecked([new RolePermission(employeeRole.Id, Stale, (int)DataScope.Self)]);

            var hr = await db.Roles.SingleAsync(r => r.Code == SystemRoleCodes.HR, Ct);
            db.Roles.Remove(hr);

            var custom = Role.CreateCustom(tenantId, "custom-auditor", "Custom", null).Value;
            custom.ReplacePermissionsUnchecked([new RolePermission(custom.Id, Stale, (int)DataScope.Company)]);
            db.Roles.Add(custom);
            customRoleId = custom.Id;
            await db.SaveChangesAsync(Ct);
        }

        ActAs(tenantId, employeeUserId);
        var permissions = Get<IPermissionService>();
        (await permissions.HasAsync(employeeUserId, EmployeePermissions.MyViewKey, Ct)).ShouldBeFalse();
        (await permissions.HasAsync(employeeUserId, Stale, Ct)).ShouldBeTrue();

        var synchronizer = Get<SystemRolePermissionSynchronizer>();
        (await synchronizer.SyncAllTenantsAsync(Ct)).ShouldBe(2);

        // Cache was invalidated: the running user sees the new definition immediately.
        (await permissions.HasAsync(employeeUserId, EmployeePermissions.MyViewKey, Ct)).ShouldBeTrue();
        (await permissions.HasAsync(employeeUserId, EmployeePermissions.DirectoryViewKey, Ct)).ShouldBeTrue();
        (await permissions.HasAsync(employeeUserId, Stale, Ct)).ShouldBeFalse();

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var catalog = Get<IPermissionCatalog>();
            var roles = await db.Roles.AsNoTracking().ToListAsync(Ct);
            roles.Single(r => r.Id == customRoleId).Permissions.Select(p => p.Permission).ShouldBe([Stale]);
            roles.Single(r => r.Code == SystemRoleCodes.HR).Permissions.Select(p => p.Permission)
                .ShouldBe(SystemRoleDefinitions.Get(SystemRoleCodes.HR).Resolve(Guid.Empty, catalog.All).Select(p => p.Permission), ignoreOrder: true);
            // Owner tüm kataloğu alır — platform (süper admin) izinleri hariç: onlar yalnız platform kiracısındaki SuperAdmin'e aittir.
            roles.Single(r => r.Code == SystemRoleCodes.Owner).Permissions.Count.ShouldBe(catalog.All.Count(p => !PlatformPermissions.IsPlatformKey(p.Key)));
        }

        (await synchronizer.SyncAllTenantsAsync(Ct)).ShouldBe(0);
    }

    /// <summary>senseik-050: existing tenants seeded before the HR/System-Administrator RBAC split still carry
    /// identity-management grants (identity.users.manage/.suspend/.reset_mfa) on their HR role row. Sync must
    /// strip those on the next run (permission-reduction scenario) while leaving identity.users.view/.invite -
    /// and every other HR permission - untouched, and the HR user must be denied identity-management commands
    /// afterwards even though they still pass AuthorizationBehaviour for permissions they keep.</summary>
    [Fact]
    public async Task SystemRoleSync_RemovesStaleIdentityManagementGrant_FromExistingHrRole()
    {
        var tenantId = await SeedTenantAsync("hr-rbac-separation");
        var hrId = await SeedUserAsync(tenantId, "hr@acme.test", SystemRoleCodes.HR);
        var targetId = await SeedUserAsync(tenantId, "target@acme.test", SystemRoleCodes.Employee);
        var db = Get<IdentityDbContext>();
        ActAs(tenantId, hrId);

        // Simulate a pre-existing tenant: grant the legacy identity-management permissions directly, bypassing
        // the (now narrower) role definition, the way a tenant seeded before this change would still have them.
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var hrRole = await db.Roles.SingleAsync(r => r.Code == SystemRoleCodes.HR, Ct);
            var legacy = hrRole.Permissions.ToList();
            legacy.Add(new RolePermission(hrRole.Id, IdentityPermissions.UsersManage, (int)DataScope.Company));
            legacy.Add(new RolePermission(hrRole.Id, IdentityPermissions.UsersSuspend, (int)DataScope.Company));
            legacy.Add(new RolePermission(hrRole.Id, IdentityPermissions.UsersResetMfa, (int)DataScope.Company));
            hrRole.ReplacePermissionsUnchecked(legacy);
            await db.SaveChangesAsync(Ct);
        }

        var permissions = Get<IPermissionService>();
        (await permissions.HasAsync(hrId, IdentityPermissions.UsersManage, Ct)).ShouldBeTrue();

        var synchronizer = Get<SystemRolePermissionSynchronizer>();
        (await synchronizer.SyncAllTenantsAsync(Ct)).ShouldBe(1);

        (await permissions.HasAsync(hrId, IdentityPermissions.UsersManage, Ct)).ShouldBeFalse();
        (await permissions.HasAsync(hrId, IdentityPermissions.UsersSuspend, Ct)).ShouldBeFalse();
        (await permissions.HasAsync(hrId, IdentityPermissions.UsersResetMfa, Ct)).ShouldBeFalse();
        (await permissions.HasAsync(hrId, IdentityPermissions.UsersView, Ct)).ShouldBeTrue();
        (await permissions.HasAsync(hrId, IdentityPermissions.UsersInvite, Ct)).ShouldBeTrue();

        ShouldFailWith(await ExecuteCommandAsync(new SetUserRolesCommand(targetId, [await RoleIdAsync(tenantId, SystemRoleCodes.HRSpecialist)])),
            ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserStatusCommand(targetId, false)), ErrorCodes.Forbidden, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new ResetUserMfaCommand(targetId)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        (await synchronizer.SyncAllTenantsAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task SelfCard_ReturnsCallersOwnEmployee_Or404()
    {
        var tenantId = await SeedTenantAsync("self-card");
        var record = await SeedEmployeeAsync(tenantId, "Nur", "Kendim", workEmail: "nur@acme.test");
        var other = await SeedEmployeeAsync(tenantId, "Başka", "Biri");
        var userId = await SeedUserAsync(tenantId, "nur@acme.test", SystemRoleCodes.Employee, record);
        var unlinkedUserId = await SeedUserAsync(tenantId, "nobody@acme.test", SystemRoleCodes.Employee);

        ActAs(tenantId, userId, record);
        var card = (await ExecuteQueryAsync(new GetMyEmployeeQuery())).Value;
        card.Id.ShouldBe(record);
        card.WorkEmail.ShouldBe("nur@acme.test");

        // A plain employee still cannot open someone else's card.
        ShouldFailWith(await ExecuteQueryAsync(new GetEmployeeQuery(other)), ErrorCodes.Forbidden, ErrorType.Forbidden);

        ActAs(tenantId, unlinkedUserId);
        ShouldFailWith(await ExecuteQueryAsync(new GetMyEmployeeQuery()), EmployeeErrors.EmployeeNotFound, ErrorType.NotFound);
    }

    private async Task<Guid> RoleIdAsync(Guid tenantId, string code)
    {
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            return await Get<IRoleRepository>().GetByCodeAsync(code, Ct) is { } role ? role.Id : Guid.Empty;
        }
    }

    private async Task<string?> AccessTypeOfAsync(Guid tenantId, Guid userId)
    {
        var accounts = await Get<Sense.Modules.Identity.Contracts.IUserAccountLookup>().GetAsync(tenantId, [userId], Ct);
        return accounts[userId].AccessType;
    }
}
