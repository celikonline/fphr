using NSubstitute;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Z5.3 canlı monitör: son okutma yönünden içeride/dışarıda, son kaynak/terminal ve terminal çevrimiçi durumu.</summary>
public sealed class LiveBoardTerminalTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);
    private static readonly DateTime NowUtc = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FixedLocalClock Clock = new(NowUtc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IPunchRepository _punches = Substitute.For<IPunchRepository>();
    private readonly ITerminalRepository _terminals = Substitute.For<ITerminalRepository>();

    public LiveBoardTerminalTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.BoardViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay>());
        _terminals.ListTerminalsAsync(Arg.Any<CancellationToken>()).Returns(new List<Terminal>());
        _terminals.ListGroupsAsync(Arg.Any<CancellationToken>()).Returns(new List<TerminalGroup>());
    }

    private void GivenPunches(params Punch[] punches) =>
        _punches.ListForRangeAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(punches.ToList());

    private Punch NewPunch(int minutesAgo, PunchDirection direction, PunchSource source = PunchSource.Device, Guid? terminalId = null)
    {
        var occurred = NowUtc.AddMinutes(-minutesAgo);
        return Punch.Create(_tenantId, _employeeA, occurred, Today, direction, source, null, terminalId, null, null, null, false, null, null, NowUtc).Value;
    }

    private Task<Sense.Shared.Kernel.Results.Result<IReadOnlyList<LiveAttendanceStatusDto>>> RunBoard() =>
        new GetLiveAttendanceBoardHandler(_tenant, _dataScope, _employees, _days, _punches, _terminals, Clock)
            .Handle(new GetLiveAttendanceBoardQuery(null), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Inside_derived_from_last_punch_direction()
    {
        // Day record says checked out, but the latest raw punch is an "in" -> inside.
        var day = AttendanceDay.Record(_tenantId, _employeeA, Today, new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.Device);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { day });
        var terminal = Terminal.Create(_tenantId, "T1", "Ana Kapı", null, null, TerminalDirectionMode.Both, "hash", "pfx").Value;
        _terminals.ListTerminalsAsync(Arg.Any<CancellationToken>()).Returns(new List<Terminal> { terminal });
        GivenPunches(NewPunch(120, PunchDirection.Out, PunchSource.Manual), NewPunch(10, PunchDirection.In, PunchSource.Terminal, terminal.Id));

        var row = (await RunBoard()).Value.ShouldHaveSingleItem();

        row.IsInside.ShouldBeTrue();
        row.LastPunchSource.ShouldBe(PunchSourceDto.Terminal);
        row.LastPunchTerminalId.ShouldBe(terminal.Id);
        row.LastPunchTerminalName.ShouldBe("Ana Kapı");
    }

    [Fact]
    public async Task Last_out_punch_reports_outside()
    {
        GivenPunches(NewPunch(120, PunchDirection.In), NewPunch(10, PunchDirection.Out, PunchSource.SelfService));

        var row = (await RunBoard()).Value.ShouldHaveSingleItem();

        row.IsInside.ShouldBeFalse();
        row.LastPunchSource.ShouldBe(PunchSourceDto.SelfService);
        row.LastPunchTerminalId.ShouldBeNull();
    }

    [Fact]
    public async Task Legacy_board_unchanged_without_punches()
    {
        var day = AttendanceDay.Record(_tenantId, _employeeA, Today, new TimeOnly(9, 0), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { day });
        GivenPunches();

        var row = (await RunBoard()).Value.ShouldHaveSingleItem();

        row.IsInside.ShouldBeTrue();
        row.Source.ShouldBe(AttendanceDaySource.SelfService);
        row.LastPunchSource.ShouldBeNull();
        row.LastPunchTerminalName.ShouldBeNull();
    }

    [Fact]
    public async Task Deleted_punch_ignored()
    {
        var deletedIn = NewPunch(5, PunchDirection.In);
        deletedIn.SoftDelete(Guid.NewGuid(), "hatalı", NowUtc).IsSuccess.ShouldBeTrue();
        GivenPunches(NewPunch(60, PunchDirection.Out, PunchSource.Manual), deletedIn);

        var row = (await RunBoard()).Value.ShouldHaveSingleItem();

        row.IsInside.ShouldBeFalse();
        row.LastPunchSource.ShouldBe(PunchSourceDto.Manual);
    }

    [Fact]
    public async Task Scope_applied()
    {
        var other = Guid.NewGuid();
        GivenPunches(
            NewPunch(10, PunchDirection.In),
            Punch.Create(_tenantId, other, NowUtc.AddMinutes(-5), Today, PunchDirection.In, PunchSource.Device, null, null, null, null, null, false, null, null, NowUtc).Value);

        var rows = (await RunBoard()).Value;

        rows.ShouldHaveSingleItem().EmployeeId.ShouldBe(_employeeA);
        await _punches.Received().ListForRangeAsync(
            _tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
            Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 1 && ids.Contains(_employeeA)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Terminal_offline_when_LastSeen_older_than_5_min()
    {
        var group = TerminalGroup.Create(_tenantId, "Bina A").Value;
        var online = Terminal.Create(_tenantId, "T1", "Ana Kapı", group.Id, null, TerminalDirectionMode.Both, "h", "p1").Value;
        online.MarkSeen(NowUtc.AddMinutes(-4));
        var offline = Terminal.Create(_tenantId, "T2", "Yan Kapı", null, null, TerminalDirectionMode.Both, "h", "p2").Value;
        offline.MarkSeen(NowUtc.AddMinutes(-6));
        var never = Terminal.Create(_tenantId, "T3", "Arka Kapı", null, null, TerminalDirectionMode.Both, "h", "p3").Value;
        _terminals.ListTerminalsAsync(Arg.Any<CancellationToken>()).Returns(new List<Terminal> { online, offline, never });
        _terminals.ListGroupsAsync(Arg.Any<CancellationToken>()).Returns(new List<TerminalGroup> { group });
        var p = Punch.Create(_tenantId, _employeeA, NowUtc.AddMinutes(-3), Today, PunchDirection.In, PunchSource.Terminal, null, online.Id, null, null, null, false, null, null, NowUtc).Value;
        _punches.ListForRangeAsync(_tenantId, Today, Today, null, Arg.Any<CancellationToken>()).Returns(new List<Punch> { p });

        var result = await new GetLiveTerminalStatusHandler(_tenant, _terminals, _punches, Clock)
            .Handle(new GetLiveTerminalStatusQuery(), TestContext.Current.CancellationToken);

        var list = result.Value;
        list.Single(t => t.Code == "T1").Online.ShouldBeTrue();
        list.Single(t => t.Code == "T1").GroupName.ShouldBe("Bina A");
        list.Single(t => t.Code == "T1").TodayPunchCount.ShouldBe(1);
        list.Single(t => t.Code == "T1").LastPunchAtUtc.ShouldBe(p.OccurredAtUtc);
        list.Single(t => t.Code == "T2").Online.ShouldBeFalse();
        list.Single(t => t.Code == "T3").Online.ShouldBeFalse();
        list.Single(t => t.Code == "T3").TodayPunchCount.ShouldBe(0);
    }
}
