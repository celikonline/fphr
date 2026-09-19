using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

/// <summary>
/// Yetki yükseltme regresyonu: <c>identity.roles.manage</c> sahibi, sahip olmadığı izinleri bir role veremez; kendi rolünü
/// düzenleyemez; sistem rollerinin izinleri kullanıcı yoluyla değiştirilemez.
/// </summary>
public sealed class RolePermissionEscalationTests
{
    private const string Held = "employee.employees.view";
    private const string NotHeld = "payroll.runs.approve";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPermissionCatalog _catalog = Substitute.For<IPermissionCatalog>();
    private readonly IPermissionCacheInvalidator _cache = Substitute.For<IPermissionCacheInvalidator>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _current = Substitute.For<ICurrentUser>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();

    public RolePermissionEscalationTests()
    {
        _catalog.Exists(Arg.Any<string>()).Returns(true);
        _tenant.TenantId.Returns(_tenantId);
        _current.UserId.Returns(_callerId);
        _current.Roles.Returns(new HashSet<string> { "Manager", "Supervisors" });
        _permissions.GetPermissionsAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<string>)new HashSet<string> { Held });
        _permissions.GetScopeAsync(_callerId, Held, Arg.Any<CancellationToken>()).Returns(DataScope.Team);
    }

    private SetRolePermissionsHandler SetHandler() =>
        new(_roles, _catalog, _cache, _tenant, _current, new RolePermissionGrantGuard(_current, _permissions));

    private Role CustomRole(string code = "Custom")
    {
        var role = Role.CreateCustom(_tenantId, code, "Custom", null).Value;
        _roles.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        return role;
    }

    private static SetRolePermissionsCommand Cmd(Role role, string permission, DataScope scope = DataScope.Team) =>
        new(role.Id, [new RolePermissionDto(permission, scope, null)]);

    [Fact]
    public async Task SetPermissions_PermissionCallerDoesNotHold_IsForbidden_AndRoleUnchanged()
    {
        var role = CustomRole();

        var result = await SetHandler().Handle(Cmd(role, NotHeld), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.RolePermissionNotHeld);
        role.Permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetPermissions_WiderScopeThanCallerHolds_IsForbidden()
    {
        var role = CustomRole();

        var result = await SetHandler().Handle(Cmd(role, Held, DataScope.Company), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.RolePermissionNotHeld);
        role.Permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetPermissions_HeldPermissionWithinScope_Succeeds()
    {
        var role = CustomRole();

        var result = await SetHandler().Handle(Cmd(role, Held), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        role.Permissions.Select(p => p.Permission).ShouldBe([Held]);
        await _cache.Received(1).InvalidateTenantAsync(_tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPermissions_OnCallersOwnRole_IsForbidden()
    {
        var role = CustomRole("Supervisors");

        var result = await SetHandler().Handle(Cmd(role, Held), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.RoleOwnRoleReadOnly);
        role.Permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetPermissions_OnSystemRole_IsReadOnly_EvenForEmptyList()
    {
        var admin = Role.CreateSystem(_tenantId, SystemRoleCodes.Admin);
        _roles.GetByIdAsync(admin.Id, Arg.Any<CancellationToken>()).Returns(admin);

        var result = await SetHandler().Handle(new SetRolePermissionsCommand(admin.Id, []), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityDomainErrors.RoleSystemReadOnly);
    }

    [Fact]
    public async Task SetPermissions_OnOwnerRole_StaysLocked()
    {
        var owner = Role.CreateSystem(_tenantId, SystemRoleCodes.Owner);
        _roles.GetByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        var result = await SetHandler().Handle(new SetRolePermissionsCommand(owner.Id, []), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityDomainErrors.RoleLocked);
    }

    [Fact]
    public async Task CreateRole_WithPermissionCallerDoesNotHold_IsForbidden_AndNothingAdded()
    {
        var handler = new CreateRoleHandler(_roles, _catalog, _tenant, new RolePermissionGrantGuard(_current, _permissions));

        var result = await handler.Handle(
            new CreateRoleCommand("Sneaky", "Sneaky", null, [new RolePermissionDto(NotHeld, DataScope.Self, null)]), CancellationToken.None);

        result.Error.Code.ShouldBe(IdentityErrors.RolePermissionNotHeld);
        _roles.DidNotReceive().Add(Arg.Any<Role>());
    }
}
