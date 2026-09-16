using Microsoft.Extensions.Logging;
using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Users;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Users;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Users;

/// <summary>
/// G-3-01: EmployeeTerminated consumer erişimi kapatır. <see cref="EmployeeTerminatedHandler"/>'ın XML doc'undaki
/// gerekçeye bkz. (KVKK/son gün belgesi - B-3-02, tek Owner koruması).
/// </summary>
public sealed class EmployeeTerminatedHandlerTests
{
    private static EmployeeTerminated Event(Guid employeeId) =>
        new(Guid.NewGuid(), employeeId, TerminationType.Resignation, "test", null);

    private static User NewUser(Guid employeeId)
    {
        var user = User.Create(Guid.NewGuid(), "eren@example.com", "Eren", "Yılmaz", "tr").Value;
        user.LinkEmployee(employeeId);
        return user;
    }

    private static EmployeeTerminatedHandler Handler(IUserRepository users, IRoleRepository roles, IIdentityUnitOfWork unitOfWork) =>
        new(users, roles, unitOfWork, Substitute.For<ILogger<EmployeeTerminatedHandler>>());

    [Fact]
    public async Task Handle_ActiveUser_DisablesAndSaves()
    {
        var employeeId = Guid.NewGuid();
        var user = NewUser(employeeId);
        var users = Substitute.For<IUserRepository>();
        users.GetByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        roles.GetByCodeAsync(SystemRoleCodes.Owner, Arg.Any<CancellationToken>()).Returns((Role?)null);
        var unitOfWork = Substitute.For<IIdentityUnitOfWork>();

        await Handler(users, roles, unitOfWork).Handle(Event(employeeId), CancellationToken.None);

        user.Status.ShouldBe(UserStatus.Disabled);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoLinkedUser_IsNoOp()
    {
        var employeeId = Guid.NewGuid();
        var users = Substitute.For<IUserRepository>();
        users.GetByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>()).Returns((User?)null);
        var roles = Substitute.For<IRoleRepository>();
        var unitOfWork = Substitute.For<IIdentityUnitOfWork>();

        await Handler(users, roles, unitOfWork).Handle(Event(employeeId), CancellationToken.None);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Redelivery_IsIdempotent()
    {
        var employeeId = Guid.NewGuid();
        var user = NewUser(employeeId);
        user.Disable();
        var users = Substitute.For<IUserRepository>();
        users.GetByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>()).Returns(user);
        var roles = Substitute.For<IRoleRepository>();
        var unitOfWork = Substitute.For<IIdentityUnitOfWork>();

        await Handler(users, roles, unitOfWork).Handle(Event(employeeId), CancellationToken.None);

        user.Status.ShouldBe(UserStatus.Disabled);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LastActiveOwner_IsPreserved()
    {
        var employeeId = Guid.NewGuid();
        var user = NewUser(employeeId);
        var ownerRole = Role.CreateSystem(user.TenantId, SystemRoleCodes.Owner);
        user.SetRoles([ownerRole.Id]);
        var users = Substitute.For<IUserRepository>();
        users.GetByEmployeeIdAsync(employeeId, Arg.Any<CancellationToken>()).Returns(user);
        users.CountActiveOwnersAsync(Arg.Any<CancellationToken>()).Returns(1);
        var roles = Substitute.For<IRoleRepository>();
        roles.GetByCodeAsync(SystemRoleCodes.Owner, Arg.Any<CancellationToken>()).Returns(ownerRole);
        var unitOfWork = Substitute.For<IIdentityUnitOfWork>();

        await Handler(users, roles, unitOfWork).Handle(Event(employeeId), CancellationToken.None);

        user.Status.ShouldNotBe(UserStatus.Disabled);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
