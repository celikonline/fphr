using NSubstitute;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>
/// Kullanıcı grubu seçici (dört grup modeli): İK Yöneticisi / Muhasebe / Çalışan'dan yalnız biri kalır, özel roller korunur.
/// Owner seçilemez; İK Yöneticisine geçişte korunur, başka gruba geçişte düşer — bunu yalnız bir Owner yapabilir ve son aktif
/// Owner düşürülemez.
/// </summary>
public sealed class SetUserAccessTypeHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPermissionCacheInvalidator _cache = Substitute.For<IPermissionCacheInvalidator>();
    private readonly Role _owner;
    private readonly Role _hr;
    private readonly Role _payroll;
    private readonly Role _employee;
    private readonly Role _custom;

    public SetUserAccessTypeHandlerTests()
    {
        _owner = SystemRole(SystemRoleCodes.Owner);
        _hr = SystemRole(SystemRoleCodes.HR);
        _payroll = SystemRole(SystemRoleCodes.PayrollSpecialist);
        _employee = SystemRole(SystemRoleCodes.Employee);
        _custom = Role.CreateCustom(_tenantId, "Supervisors", "Supervisors", null).Value;
        _users.CountActiveOwnersAsync(Arg.Any<CancellationToken>()).Returns(2);
    }

    private SetUserAccessTypeHandler Handler() =>
        new(_users, _roles, new AccessTypeGuard(_currentUser, _users, _roles), _cache);

    private Role SystemRole(string code)
    {
        var role = Role.CreateSystem(_tenantId, code);
        _roles.GetByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(role);
        return role;
    }

    private User ActiveUser(string email, params Role[] roles)
    {
        var user = User.Create(_tenantId, email, "Test", "User", "tr").Value;
        user.SetPassword("hash");
        user.SetRoles([.. roles.Select(r => r.Id)]);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    private void CallerIs(User caller) => _currentUser.UserId.Returns(caller.Id);

    private static Guid[] RoleIds(User user) => [.. user.Roles.Select(r => r.RoleId)];

    [Fact]
    public async Task OwnerMovedToHr_KeepsOwnerFlag_AndCustomRoles()
    {
        // Arrange
        var caller = ActiveUser("caller@acme.test", _owner, _hr);
        CallerIs(caller);
        var target = ActiveUser("target@acme.test", _owner, _payroll, _custom);

        // Act
        var result = await Handler().Handle(new SetUserAccessTypeCommand(target.Id, SystemRoleCodes.HR), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        RoleIds(target).ShouldBe([_owner.Id, _hr.Id, _custom.Id], ignoreOrder: true);
        await _cache.Received(1).InvalidateUserAsync(target.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnerMovedToEmployee_ByAnotherOwner_DropsOwnerFlag()
    {
        var caller = ActiveUser("caller@acme.test", _owner, _hr);
        CallerIs(caller);
        var target = ActiveUser("target@acme.test", _owner, _hr, _custom);

        var result = await Handler().Handle(new SetUserAccessTypeCommand(target.Id, SystemRoleCodes.Employee), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        RoleIds(target).ShouldBe([_employee.Id, _custom.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task OwnerMovedToPayroll_ByNonOwner_IsForbidden_AndRolesUnchanged()
    {
        // İK Yöneticisi (Owner olmayan) identity.users.manage taşısa da hesap sahibini başka gruba taşıyamaz.
        var caller = ActiveUser("caller@acme.test", _hr);
        CallerIs(caller);
        var target = ActiveUser("target@acme.test", _owner, _hr);

        var result = await Handler().Handle(new SetUserAccessTypeCommand(target.Id, SystemRoleCodes.PayrollSpecialist), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Code.ShouldBe(IdentityErrors.AccessTypeOwnerOnly);
        RoleIds(target).ShouldBe([_owner.Id, _hr.Id], ignoreOrder: true);
        await _cache.DidNotReceiveWithAnyArgs().InvalidateUserAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LastActiveOwner_MovedToEmployee_IsRejected_AndRolesUnchanged()
    {
        var owner = ActiveUser("owner@acme.test", _owner, _hr);
        CallerIs(owner);
        _users.CountActiveOwnersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handler().Handle(new SetUserAccessTypeCommand(owner.Id, SystemRoleCodes.Employee), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Rule);
        result.Error.Code.ShouldBe(IdentityErrors.UserLastOwner);
        RoleIds(owner).ShouldBe([_owner.Id, _hr.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task LastActiveOwner_MovedToHr_Succeeds_BecauseOwnerIsKept()
    {
        var owner = ActiveUser("owner@acme.test", _owner, _payroll);
        CallerIs(owner);
        _users.CountActiveOwnersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handler().Handle(new SetUserAccessTypeCommand(owner.Id, SystemRoleCodes.HR), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        RoleIds(owner).ShouldBe([_owner.Id, _hr.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task NonOwnerTarget_SwitchesGroup_WithoutOwnerCheck()
    {
        // Owner değişmiyorsa çağıranın Owner olması gerekmez (Muhasebe → İK Yöneticisi; Owner eklenmez).
        var caller = ActiveUser("caller@acme.test", _payroll);
        CallerIs(caller);
        var target = ActiveUser("target@acme.test", _employee, _custom);

        var result = await Handler().Handle(new SetUserAccessTypeCommand(target.Id, SystemRoleCodes.HR), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        RoleIds(target).ShouldBe([_hr.Id, _custom.Id], ignoreOrder: true);
        await _users.DidNotReceiveWithAnyArgs().CountActiveOwnersAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(SystemRoleCodes.HR, true)]
    [InlineData(SystemRoleCodes.PayrollSpecialist, true)]
    [InlineData(SystemRoleCodes.Employee, true)]
    [InlineData(SystemRoleCodes.Owner, false)]
    [InlineData(SystemRoleCodes.Admin, false)]
    [InlineData(SystemRoleCodes.Manager, false)]
    [InlineData(SystemRoleCodes.SuperAdmin, false)]
    public void Validator_AcceptsOnlyTheThreeUserGroups(string accessType, bool valid)
    {
        var result = new SetUserAccessTypeValidator().Validate(new SetUserAccessTypeCommand(Guid.NewGuid(), accessType));

        result.IsValid.ShouldBe(valid);
    }

    [Fact]
    public async Task Guard_NonOwnerCannotGrantOwner_ButOwnerCan()
    {
        var nonOwner = ActiveUser("hr@acme.test", _hr);
        var owner = ActiveUser("owner@acme.test", _owner, _hr);
        var guard = new AccessTypeGuard(_currentUser, _users, _roles);

        CallerIs(nonOwner);
        var denied = await guard.EnsureCanChangeAsync([_hr.Id], [_hr.Id, _owner.Id], CancellationToken.None);
        CallerIs(owner);
        var allowed = await guard.EnsureCanChangeAsync([_hr.Id], [_hr.Id, _owner.Id], CancellationToken.None);

        denied.Error.Code.ShouldBe(IdentityErrors.AccessTypeOwnerOnly);
        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Guard_NonOwnerCanChangeNonOwnerGroups()
    {
        // Eski modeldeki Admin koruması kalktı: Owner dışındaki grup değişiklikleri UsersManage ile serbest.
        var caller = ActiveUser("payroll@acme.test", _payroll);
        CallerIs(caller);
        var guard = new AccessTypeGuard(_currentUser, _users, _roles);

        var result = await guard.EnsureCanChangeAsync([_employee.Id], [_hr.Id], CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
