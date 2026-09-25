using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Employee.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Recalculation;

public sealed class AttendanceDayRecalculationTests
{
    private static readonly DateOnly September1 = new(2026, 9, 1);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly List<AttendanceDay> _store = [];
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceUnitOfWork _uow = Substitute.For<IAttendanceUnitOfWork>();
    private readonly IDayRebuilder _rebuilder = Substitute.For<IDayRebuilder>();

    public AttendanceDayRecalculationTests()
    {
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IReadOnlyCollection<Guid>>();
                return _store.Where(d => ids.Contains(d.EmployeeId)).ToList();
            });
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        // Rebuild = re-derive the day against a new plan (09:00-17:00), which resets override/resolution inside Recompute.
        _rebuilder.RebuildAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var day = _store.FirstOrDefault(d => d.EmployeeId == call.ArgAt<Guid>(1) && d.Date == call.ArgAt<DateOnly>(2));
                day?.UpdateRecord(day.CheckIn, day.CheckOut, new TimeOnly(9, 0), new TimeOnly(17, 0), 480, 0, AttendanceDaySource.Manual);
                return Task.FromResult(day);
            });
    }

    private RecalculationExecutor Executor() => new(_days, _approvals, _rebuilder, _uow, _employees);

    private AttendanceDay AddDay(Guid employeeId, DateOnly date)
    {
        var day = AttendanceDay.Record(_tenantId, employeeId, date, new TimeOnly(10, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 480, 0, AttendanceDaySource.Manual);
        _store.Add(day);
        return day;
    }

    [Fact]
    public async Task Skips_days_in_approved_month()
    {
        var employee = Guid.NewGuid();
        AddDay(employee, September1.AddDays(2));
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), September1, Arg.Any<CancellationToken>())
            .Returns([MonthlyTimesheetApproval.Approve(_tenantId, employee, September1, DateTime.UtcNow, null)]);

        var rebuilt = await Executor().RunAsync(_tenantId, September1, September1.AddDays(9), [employee], null, TestContext.Current.CancellationToken);

        rebuilt.ShouldBe(0);
        await _rebuilder.DidNotReceive().RebuildAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preserves_resolved_state_and_override()
    {
        var employee = Guid.NewGuid();
        var resolved = AddDay(employee, September1.AddDays(1));
        resolved.Anomalies.ShouldNotBe(AttendanceAnomaly.None);
        resolved.ResolveWithOverride(400, "manuel").IsSuccess.ShouldBeTrue();
        var ignored = AddDay(employee, September1.AddDays(2));
        ignored.Ignore().IsSuccess.ShouldBeTrue();
        var untouched = AddDay(employee, September1.AddDays(3));

        await Executor().RunAsync(_tenantId, September1, September1.AddDays(5), [employee], null, TestContext.Current.CancellationToken);

        resolved.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        resolved.OverrideWorkedMinutes.ShouldBe(400);
        resolved.OverrideReason.ShouldBe("manuel");
        ignored.ResolutionState.ShouldBe(AttendanceResolutionState.Ignored);
        untouched.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
    }

    [Fact]
    public async Task Is_idempotent_when_run_twice()
    {
        var employee = Guid.NewGuid();
        var day = AddDay(employee, September1.AddDays(1));
        day.ResolveWithOverride(300, "x");
        var executor = Executor();

        var first = await executor.RunAsync(_tenantId, September1, September1.AddDays(5), [employee], null, TestContext.Current.CancellationToken);
        var snapshot = (day.Anomalies, day.ResolutionState, day.OverrideWorkedMinutes, day.WorkedMinutes, day.NetWorkedMinutes);
        var second = await executor.RunAsync(_tenantId, September1, September1.AddDays(5), [employee], null, TestContext.Current.CancellationToken);

        second.ShouldBe(first);
        (day.Anomalies, day.ResolutionState, day.OverrideWorkedMinutes, day.WorkedMinutes, day.NetWorkedMinutes).ShouldBe(snapshot);
        _store.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Range_over_31_days_is_chunked_by_employee()
    {
        var everyone = Enumerable.Range(0, 450).Select(_ => Guid.NewGuid()).ToList();
        _employees.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(everyone);
        var progress = new List<(int Total, int Done)>();

        await Executor().RunAsync(_tenantId, September1, September1.AddDays(30), null, (t, d) => progress.Add((t, d)), TestContext.Current.CancellationToken);

        RecalculationExecutor.ChunkSize.ShouldBe(200);
        await _uow.Received(3).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _days.Received(2).ListForEmployeesAsync(Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 200), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _days.Received(1).ListForEmployeesAsync(Arg.Is<IReadOnlyCollection<Guid>>(c => c.Count == 50), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        progress.Last().ShouldBe((450 * 31, 450 * 31));
    }
}
