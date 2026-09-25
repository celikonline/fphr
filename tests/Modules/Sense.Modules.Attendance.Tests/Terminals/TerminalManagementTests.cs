using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Terminal/grup yönetimi (Z5.1): izinler (izinsiz 403), kod tekilliği, grup/lokasyon doğrulaması, çevrimiçi durumu, dolu grup silinemez.</summary>
public sealed class TerminalManagementTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ILocationLookup _locations = Substitute.For<ILocationLookup>();

    public TerminalManagementTests() => _tenant.TenantId.Returns(_tenantId);

    public static TheoryData<Type, string> RequestPermissions()
    {
        var data = new TheoryData<Type, string>();
        foreach (var type in typeof(TerminalPunchIngest).Assembly.GetTypes().Where(t => t.Namespace == typeof(TerminalPunchIngest).Namespace && IsRequest(t)))
        {
            data.Add(type, IsQuery(type) ? AttendancePermissions.TerminalView.Key : AttendancePermissions.TerminalManage.Key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RequestPermissions))]
    public void Every_terminal_request_requires_the_terminal_permission(Type request, string permission)
    {
        request.GetCustomAttributes<RequiresPermissionAttribute>().Select(a => a.Permission).ShouldBe([permission]);
    }

    [Fact]
    public async Task Management_without_permission_is_forbidden()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(default, default!, default).ReturnsForAnyArgs(false);
        var behaviour = new AuthorizationBehaviour<CreateTerminalCommand, Result<TerminalApiKeyDto>>(user, permissions, Substitute.For<IDataScopeService>());
        var handlerRan = false;

        var result = await behaviour.Handle(
            new CreateTerminalCommand(new TerminalInput("T", "Ana", null, null, TerminalDirectionMode.Both)),
            () =>
            {
                handlerRan = true;
                return Task.FromResult<Result<TerminalApiKeyDto>>(new TerminalApiKeyDto(Guid.NewGuid(), "x", "x"));
            },
            TestContext.Current.CancellationToken);

        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        handlerRan.ShouldBeFalse();
        await permissions.Received().HasAsync(Arg.Any<Guid>(), AttendancePermissions.TerminalManage.Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_missing_group_and_unknown_location()
    {
        var group = Guid.NewGuid();
        var location = Guid.NewGuid();
        _terminals.TerminalCodeExistsAsync("DUP", null, Arg.Any<CancellationToken>()).Returns(true);
        _terminals.GetGroupByIdAsync(group, Arg.Any<CancellationToken>()).Returns(Error.NotFound(AttendanceDomainErrors.TerminalGroupNotFound));
        _locations.ExistsActiveAsync(_tenantId, location, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateTerminalHandler(_terminals, _locations, _tenant);

        var duplicate = await handler.Handle(new CreateTerminalCommand(new TerminalInput("DUP", "Ana", null, null, TerminalDirectionMode.Both)), TestContext.Current.CancellationToken);
        var noGroup = await handler.Handle(new CreateTerminalCommand(new TerminalInput("T2", "Ana", group, null, TerminalDirectionMode.Both)), TestContext.Current.CancellationToken);
        var noLocation = await handler.Handle(new CreateTerminalCommand(new TerminalInput("T3", "Ana", null, location, TerminalDirectionMode.Both)), TestContext.Current.CancellationToken);

        duplicate.Error.Code.ShouldBe(TerminalErrors.CodeDuplicate);
        noGroup.Error.Code.ShouldBe(AttendanceDomainErrors.TerminalGroupNotFound);
        noLocation.Error.Code.ShouldBe(TerminalErrors.LocationNotFound);
        _terminals.DidNotReceiveWithAnyArgs().AddTerminal(default!);
    }

    [Fact]
    public async Task Update_binds_location_group_and_direction_mode()
    {
        var terminal = TerminalTestData.NewTerminal(_tenantId, TerminalApiKey.Generate().Key);
        var group = TerminalGroup.Create(_tenantId, "Merkez").Value;
        var location = Guid.NewGuid();
        _terminals.GetTerminalByIdAsync(terminal.Id, Arg.Any<CancellationToken>()).Returns(terminal);
        _terminals.GetGroupByIdAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        _locations.ExistsActiveAsync(_tenantId, location, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new UpdateTerminalHandler(_terminals, _locations, _tenant).Handle(
            new UpdateTerminalCommand(terminal.Id, new TerminalInput("T-02", "Yan kapı", group.Id, location, TerminalDirectionMode.OutOnly)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (terminal.Code, terminal.TerminalGroupId, terminal.LocationId, terminal.DirectionMode).ShouldBe(("T-02", group.Id, location, TerminalDirectionMode.OutOnly));
        await _terminals.Received().TerminalCodeExistsAsync("T-02", terminal.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_reports_online_when_last_seen_within_five_minutes()
    {
        var online = TerminalTestData.NewTerminal(_tenantId, TerminalApiKey.Generate().Key, code: "ON");
        online.MarkSeen(Now.AddMinutes(-4));
        var offline = TerminalTestData.NewTerminal(_tenantId, TerminalApiKey.Generate().Key, code: "OFF");
        offline.MarkSeen(Now.AddMinutes(-5));
        var never = TerminalTestData.NewTerminal(_tenantId, TerminalApiKey.Generate().Key, code: "NEW");
        _terminals.ListTerminalsAsync(Arg.Any<CancellationToken>()).Returns([online, offline, never]);

        var list = (await new ListTerminalsHandler(_terminals, new FakeTimeProvider(Now)).Handle(new ListTerminalsQuery(), TestContext.Current.CancellationToken)).Value;

        list.Select(t => (t.Code, t.IsOnline)).ShouldBe([("ON", true), ("OFF", false), ("NEW", false)]);
        list[0].LastSeenAtUtc.ShouldBe(Now.AddMinutes(-4));
    }

    [Fact]
    public async Task Deactivate_and_reactivate_terminal()
    {
        var terminal = TerminalTestData.NewTerminal(_tenantId, TerminalApiKey.Generate().Key);
        _terminals.GetTerminalByIdAsync(terminal.Id, Arg.Any<CancellationToken>()).Returns(terminal);
        var handler = new SetTerminalActiveHandler(_terminals);

        (await handler.Handle(new SetTerminalActiveCommand(terminal.Id, false), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        terminal.IsActive.ShouldBeFalse();
        (await handler.Handle(new SetTerminalActiveCommand(terminal.Id, true), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        terminal.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Group_with_terminals_cannot_be_deleted_empty_group_can()
    {
        var busy = TerminalGroup.Create(_tenantId, "Dolu").Value;
        var empty = TerminalGroup.Create(_tenantId, "Boş").Value;
        _terminals.GetGroupByIdAsync(busy.Id, Arg.Any<CancellationToken>()).Returns(busy);
        _terminals.GetGroupByIdAsync(empty.Id, Arg.Any<CancellationToken>()).Returns(empty);
        _terminals.AnyTerminalInGroupAsync(busy.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteTerminalGroupHandler(_terminals);

        (await handler.Handle(new DeleteTerminalGroupCommand(busy.Id), TestContext.Current.CancellationToken)).Error.Code.ShouldBe(TerminalErrors.GroupInUse);
        (await handler.Handle(new DeleteTerminalGroupCommand(empty.Id), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        _terminals.Received(1).RemoveGroup(empty);
        _terminals.DidNotReceive().RemoveGroup(busy);
    }

    [Fact]
    public async Task Group_create_and_rename()
    {
        TerminalGroup? added = null;
        _terminals.AddGroup(Arg.Do<TerminalGroup>(g => added = g));

        var id = await new CreateTerminalGroupHandler(_terminals, _tenant).Handle(new CreateTerminalGroupCommand(" Merkez "), TestContext.Current.CancellationToken);
        _terminals.GetGroupByIdAsync(id.Value, Arg.Any<CancellationToken>()).Returns(added!);
        var renamed = await new RenameTerminalGroupHandler(_terminals).Handle(new RenameTerminalGroupCommand(id.Value, "Fabrika"), TestContext.Current.CancellationToken);
        var invalid = await new CreateTerminalGroupHandler(_terminals, _tenant).Handle(new CreateTerminalGroupCommand(" "), TestContext.Current.CancellationToken);

        (added!.TenantId, added.Name).ShouldBe((_tenantId, "Fabrika"));
        renamed.IsSuccess.ShouldBeTrue();
        invalid.Error.Code.ShouldBe(AttendanceDomainErrors.TerminalGroupNameRequired);
    }

    private static bool IsRequest(Type type) => !type.IsAbstract && (IsQuery(type) || type.GetInterfaces().Any(i => i == typeof(ICommand) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))));

    private static bool IsQuery(Type type) => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
}
