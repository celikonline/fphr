using NSubstitute;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>GET attendance/board/live (S-31 kabul kriteri 3): canlı içeride/dışarıda panosu.</summary>
public sealed class LiveAttendanceBoardFeaturesTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeB = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();

    public LiveAttendanceBoardFeaturesTests() => _tenant.TenantId.Returns(_tenantId);

    private GetLiveAttendanceBoardHandler CreateHandler() => new(_tenant, _dataScope, _employees, _days, Clock);

    [Fact]
    public async Task Handle_NoVisibleEmployees_ReturnsEmpty()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.BoardViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var result = await CreateHandler().Handle(new GetLiveAttendanceBoardQuery(null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_CheckedInWithNoCheckOut_ReportsInside()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.BoardViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });
        var day = AttendanceDay.Record(_tenantId, _employeeA, Today, new TimeOnly(9, 0), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Today, Today, Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { day });

        var result = await CreateHandler().Handle(new GetLiveAttendanceBoardQuery(null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var row = result.Value.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe("Ayşe Yılmaz");
        row.IsInside.ShouldBeTrue();
        row.CheckIn.ShouldBe(new TimeOnly(9, 0));
        row.CheckOut.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_CheckedInAndOut_ReportsOutside()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.BoardViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });
        var day = AttendanceDay.Record(_tenantId, _employeeA, Today, new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Today, Today, Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { day });

        var result = await CreateHandler().Handle(new GetLiveAttendanceBoardQuery(null), TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem().IsInside.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_EmployeeWithNoRecordToday_ReportsOutsideWithNullTimes()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.BoardViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA, _employeeB });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact>
            {
                new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active"),
                new(_employeeB, "Kemal", "Demir", null, null, null, "Active"),
            });
        var day = AttendanceDay.Record(_tenantId, _employeeA, Today, new TimeOnly(9, 0), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Today, Today, Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { day });

        var result = await CreateHandler().Handle(new GetLiveAttendanceBoardQuery(null), TestContext.Current.CancellationToken);

        result.Value.Count.ShouldBe(2);
        var absent = result.Value.Single(r => r.EmployeeId == _employeeB);
        absent.IsInside.ShouldBeFalse();
        absent.CheckIn.ShouldBeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
