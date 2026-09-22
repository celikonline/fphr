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
    /// <summary>Dört kullanıcı grubu: İK Yöneticisi ve Muhasebe kullanıcıları gruplar arası taşıyabilir; hesap sahibi (Owner)
    /// seçilemez, İK Yöneticisiyle birlikte taşınır ve yalnız bir Owner düşürebilir; son aktif Owner düşürülemez.</summary>
    [Fact]
    public async Task UserGroup_HrAndAccountingManageGroups_OwnerFlagTravelsWithHr_OnlyOwnerDropsIt_LastOwnerKept()
    {
        var tenantId = await SeedTenantAsync("access-type");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var hrId = await SeedUserAsync(tenantId, "hr@acme.test", SystemRoleCodes.HR);
        var accountingId = await SeedUserAsync(tenantId, "muhasebe@acme.test", SystemRoleCodes.PayrollSpecialist);
        var targetId = await SeedUserAsync(tenantId, "target@acme.test", SystemRoleCodes.Employee);
        var ownerRoleId = await RoleIdAsync(tenantId, SystemRoleCodes.Owner);

        // Owner alone: stepping out of İK Yöneticisi would drop the last Owner flag.
        ActAs(tenantId, ownerId);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.Employee)), IdentityErrors.UserLastOwner, ErrorType.Rule);
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.HR))).IsSuccess.ShouldBeTrue();
        (await AccessTypeOfAsync(tenantId, ownerId)).ShouldBe(SystemRoleCodes.HR);

        // Owner is not a selectable group.
        await Should.ThrowAsync<FluentValidation.ValidationException>(() => ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Owner)));

        ActAs(tenantId, hrId);
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.PayrollSpecialist))).IsSuccess.ShouldBeTrue();
        (await AccessTypeOfAsync(tenantId, targetId)).ShouldBe(SystemRoleCodes.PayrollSpecialist);

        ActAs(tenantId, accountingId);
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.HR))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(targetId, SystemRoleCodes.Employee))).IsSuccess.ShouldBeTrue();
        (await AccessTypeOfAsync(tenantId, targetId)).ShouldBe(SystemRoleCodes.Employee);

        // Only an Owner grants or removes the Owner flag, via any role-assignment path.
        ShouldFailWith(await ExecuteCommandAsync(new SetUserRolesCommand(targetId, [ownerRoleId])), IdentityErrors.AccessTypeOwnerOnly, ErrorType.Forbidden);
        ShouldFailWith(await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.Employee)), IdentityErrors.AccessTypeOwnerOnly, ErrorType.Forbidden);

        // A second owner lets the first one step down; the remaining one is then the last owner again.
        ActAs(tenantId, ownerId);
        (await ExecuteCommandAsync(new SetUserRolesCommand(targetId, [ownerRoleId, await RoleIdAsync(tenantId, SystemRoleCodes.HR)]))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new SetUserAccessTypeCommand(ownerId, SystemRoleCodes.PayrollSpecialist))).IsSuccess.ShouldBeTrue();
        (await AccessTypeOfAsync(tenantId, ownerId)).ShouldBe(SystemRoleCodes.PayrollSpecialist);
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

    /// <summary>Dört gruba geçiş: mevcut kiracıdaki eski sistem rollerinin kullanıcıları devralan gruba taşınır, eski roller
    /// silinir, İK Yöneticisi olmayan hesap sahibine İK Yöneticisi eklenir; ikinci senkronizasyon hiçbir şey değiştirmez.</summary>
    [Fact]
    public async Task SystemRoleSync_MovesUsersOfRetiredRolesToTheirGroup_RemovesRetiredRoles_AndGivesOwnersHr()
    {
        var tenantId = await SeedTenantAsync("retired-roles");
        var ownerId = await SeedUserAsync(tenantId, "owner@acme.test", SystemRoleCodes.Owner);
        var managerId = await SeedUserAsync(tenantId, "manager@acme.test", null);
        var financeId = await SeedUserAsync(tenantId, "finance@acme.test", null);
        var specialistId = await SeedUserAsync(tenantId, "specialist@acme.test", null);
        var db = Get<IdentityDbContext>();

        // Simulate a tenant seeded before the four groups: retired system roles still exist and hold users.
        using (TenantContextSetter!.BeginScope(tenantId))
        {
            foreach (var (userId, code) in new[] { (managerId, SystemRoleCodes.Interviewer), (financeId, SystemRoleCodes.Finance), (specialistId, SystemRoleCodes.HRSpecialist) })
            {
                var retired = Role.CreateSystem(tenantId, code);
                db.Roles.Add(retired);
                var user = await db.Users.SingleAsync(u => u.Id == userId, Ct);
                user.SetRoles([retired.Id]);
            }

            await db.SaveChangesAsync(Ct);
        }

        var synchronizer = Get<SystemRolePermissionSynchronizer>();
        (await synchronizer.SyncAllTenantsAsync(Ct)).ShouldBeGreaterThan(0);

        ActAs(tenantId, ownerId);
        (await AccessTypeOfAsync(tenantId, managerId)).ShouldBe(SystemRoleCodes.Employee);
        (await AccessTypeOfAsync(tenantId, financeId)).ShouldBe(SystemRoleCodes.PayrollSpecialist);
        (await AccessTypeOfAsync(tenantId, specialistId)).ShouldBe(SystemRoleCodes.HR);
        (await AccessTypeOfAsync(tenantId, ownerId)).ShouldBe(SystemRoleCodes.HR);

        using (TenantContextSetter!.BeginScope(tenantId))
        {
            var codes = await db.Roles.AsNoTracking().Where(r => r.IsSystem).Select(r => r.Code).ToListAsync(Ct);
            codes.ShouldBe(SystemRoleCodes.All, ignoreOrder: true);
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerId, Ct)).Roles.Count.ShouldBe(2);
        }

        var permissions = Get<IPermissionService>();
        (await permissions.HasAsync(financeId, IdentityPermissions.UsersManage, Ct)).ShouldBeTrue();
        (await permissions.HasAsync(managerId, IdentityPermissions.UsersManage, Ct)).ShouldBeFalse();

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
