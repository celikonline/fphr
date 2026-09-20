using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

/// <summary>
/// Süper admin izni (<c>platform.tenants.manage</c>) müşteri kiracılarının hiçbir sistem rolüne — Owner/Admin jokeri, ITAdmin
/// "identity.*", Auditor/ApiIntegration "*.view" dahil — sızmamalı; yalnız platformdaki SuperAdmin rolü alır.
/// </summary>
public sealed class PlatformPermissionTests
{
    private static readonly IReadOnlyList<Permission> Catalog =
    [
        .. IdentityPermissions.All,
        .. new[] { "employee.employees.read", "employee.directory.view", "organization.units.read", "reports.payroll.view" }
            .Select(k => new Permission(k, k.Split('.')[0], k, "Test")),
    ];

    [Fact]
    public void Catalog_ContainsThePlatformPermission()
    {
        Catalog.ShouldContain(p => p.Key == PlatformPermissions.TenantsManage);
        PlatformPermissions.IsPlatformKey(PlatformPermissions.TenantsManage).ShouldBeTrue();
        PlatformPermissions.IsPlatformKey(IdentityPermissions.TenantManage).ShouldBeFalse();
    }

    [Fact]
    public void OwnerLockedRole_GetsEveryPermission_ExceptPlatformOnes()
    {
        var owner = Role.CreateSystem(Guid.NewGuid(), SystemRoleCodes.Owner);

        var keys = SystemRolePermissionResolver.Resolve(owner, Catalog).Select(p => p.Permission).ToList();

        keys.ShouldContain(IdentityPermissions.UsersManage);
        keys.ShouldContain(IdentityPermissions.UsersImpersonate);
        keys.ShouldNotContain(PlatformPermissions.TenantsManage);
    }

    [Theory]
    [MemberData(nameof(CustomerRoleCodes))]
    public void NoCustomerSystemRole_GetsPlatformPermissions(string roleCode)
    {
        var role = Role.CreateSystem(Guid.NewGuid(), roleCode);

        var keys = SystemRolePermissionResolver.Resolve(role, Catalog).Select(p => p.Permission).ToList();

        keys.ShouldNotContain(PlatformPermissions.TenantsManage);
    }

    [Fact]
    public void SuperAdmin_GetsOnlyPlatformPermissions()
    {
        var role = Role.CreateSystem(Guid.NewGuid(), SystemRoleCodes.SuperAdmin);

        var keys = SystemRolePermissionResolver.Resolve(role, Catalog).Select(p => p.Permission).ToList();

        keys.ShouldBe([PlatformPermissions.TenantsManage]);
    }

    [Fact]
    public void SuperAdminRole_ExistsOnlyInThePlatformTenant()
    {
        SystemRoleCodes.ForTenant(isPlatformTenant: false).ShouldNotContain(SystemRoleCodes.SuperAdmin);
        SystemRoleCodes.ForTenant(isPlatformTenant: true).ShouldContain(SystemRoleCodes.SuperAdmin);
        SystemRoleCodes.ForTenant(isPlatformTenant: true).ShouldBe([.. SystemRoleCodes.All, SystemRoleCodes.SuperAdmin]);
        SystemRoleCodes.All.ShouldNotContain(SystemRoleCodes.SuperAdmin);
    }

    [Fact]
    public void CustomRole_CannotReuseTheReservedSuperAdminCode()
    {
        var result = Role.CreateCustom(Guid.NewGuid(), "superadmin", "Süper", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityDomainErrors.RoleSystemCode);
    }

    public static IEnumerable<object[]> CustomerRoleCodes() => SystemRoleCodes.All.Select(code => new object[] { code });
}
