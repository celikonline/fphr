using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;
using Sense.Modules.Attendance.Infrastructure.Jobs;

namespace Sense.Modules.Attendance.Tests.Recalculation;

public sealed class RecalculationJobAndScopeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Job_waits_start_delay_before_reading_definitions()
    {
        var employee = Guid.NewGuid();
        var days = Substitute.For<IAttendanceDayRepository>();
        days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        var approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
        approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        var rebuilder = Substitute.For<IDayRebuilder>();
        var executor = new RecalculationExecutor(days, approvals, rebuilder, Substitute.For<IAttendanceUnitOfWork>(), Substitute.For<IEmployeeDirectory>());
        var services = new ServiceCollection()
            .AddSingleton(Substitute.For<ITenantContextSetter>())
            .AddSingleton(executor)
            .BuildServiceProvider();
        var clock = new FakeTimeProvider();
        var store = new RecalculationJobStore();
        var recalculator = new AttendanceDayRecalculator(services.GetRequiredService<IServiceScopeFactory>(), store, clock, NullLogger<AttendanceDayRecalculator>.Instance);
        var job = store.Create(_tenantId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), [employee]);

        var run = recalculator.RunJobAsync(job.JobId, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2.9));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        run.IsCompleted.ShouldBeFalse();
        store.FindById(job.JobId)!.State.ShouldBe(RecalculationJob.Queued);
        await rebuilder.DidNotReceive().RebuildAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

        clock.Advance(TimeSpan.FromSeconds(0.2));
        await run;

        AttendanceDayRecalculator.StartDelay.ShouldBe(TimeSpan.FromSeconds(3));
        store.FindById(job.JobId)!.State.ShouldBe(RecalculationJob.Completed);
    }

    [Fact]
    public async Task Rebuild_applies_shift_definition_tolerances()
    {
        var employee = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 3);
        var day = AttendanceDay.Record(_tenantId, employee, date, new TimeOnly(9, 10), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 480, 0, AttendanceDaySource.Manual);
        day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeTrue();
        var days = Substitute.For<IAttendanceDayRepository>();
        days.FindByEmployeeAndDateAsync(employee, date, Arg.Any<CancellationToken>()).Returns(day);
        var planner = Substitute.For<IPlannedDayResolver>();
        planner.ResolveAsync(employee, date, Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>()).Returns(
            new PlannedDay(new TimeOnly(9, 0), new TimeOnly(17, 0), 480, 0, Rules: new ShiftRules(15, 0, null, null, null, null, 0), ShiftDefinitionId: Guid.NewGuid()));
        var settings = Substitute.For<IShiftSettingsRepository>();
        var rebuilder = new PunchDayRebuilder(
            days, new InMemoryPunchRepository(), planner, Substitute.For<IMonthlyTimesheetApprovalRepository>(), settings,
            new FixedLocalClock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc)));

        var rebuilt = await rebuilder.RebuildAsync(_tenantId, employee, date, TestContext.Current.CancellationToken);

        rebuilt.ShouldBeSameAs(day);
        day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse();
    }

    [Fact]
    public async Task Enqueue_filters_requested_employees_by_data_scope_and_reports_skipped()
    {
        var visible = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        var recalculator = Substitute.For<IAttendanceDayRecalculator>();
        recalculator.EnqueueAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var scope = Substitute.For<IDataScopeService>();
        scope.GetVisibleEmployeeIdsAsync("attendance.recalculate.run", Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { visible });
        var handler = new EnqueueRecalculationHandler(recalculator, tenant, scope, Substitute.For<IEmployeeDirectory>());

        var result = await handler.Handle(
            new EnqueueRecalculationCommand(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), [visible, hidden]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SkippedOutOfScope.ShouldBe(1);
        await recalculator.Received(1).EnqueueAsync(
            _tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids!.Count == 1 && ids.Contains(visible)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enqueue_without_ids_uses_only_active_employees_in_scope()
    {
        var inScopeActive = Guid.NewGuid();
        var inScopeInactive = Guid.NewGuid();
        var outOfScopeActive = Guid.NewGuid();
        var recalculator = Substitute.For<IAttendanceDayRecalculator>();
        recalculator.EnqueueAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var scope = Substitute.For<IDataScopeService>();
        scope.GetVisibleEmployeeIdsAsync("attendance.recalculate.run", Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { inScopeActive, inScopeInactive });
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>()).Returns([inScopeActive, outOfScopeActive]);
        var handler = new EnqueueRecalculationHandler(recalculator, tenant, scope, directory);

        var result = await handler.Handle(new EnqueueRecalculationCommand(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await recalculator.Received(1).EnqueueAsync(
            _tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids!.Count == 1 && ids.Contains(inScopeActive)), Arg.Any<CancellationToken>());
    }
}
