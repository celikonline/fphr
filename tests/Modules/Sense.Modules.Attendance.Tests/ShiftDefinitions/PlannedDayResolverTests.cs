using Sense.Modules.Attendance.Application;
using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.ShiftDefinitions.ShiftDefinitionFixtures;

namespace Sense.Modules.Attendance.Tests.ShiftDefinitions;

/// <summary>K2 plan çözümleme önceliği: ShiftAssignment (ShiftDefinition kurallarıyla) → WorkSchedule günü.</summary>
public sealed class PlannedDayResolverTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IShiftAssignmentRepository _assignments = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftDefinitionRepository _definitions = Substitute.For<IShiftDefinitionRepository>();
    private readonly PlannedDayResolver _resolver;

    public PlannedDayResolverTests()
    {
        _schedules.GetForDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), false));
        _resolver = new PlannedDayResolver(_schedules, _assignments, _definitions);
    }

    private ShiftAssignment Assign(TimeOnly start, TimeOnly end, int breakMinutes, Guid? definitionId = null, ShiftDayType dayType = ShiftDayType.Work)
    {
        var assignment = ShiftAssignment.Create(Tenant, _employeeId, Day, start, end, breakMinutes, false, null).Value;
        assignment.SetPlan(definitionId, dayType);
        _assignments.FindByEmployeeAndDateAsync(_employeeId, Day, Arg.Any<CancellationToken>()).Returns(assignment);
        return assignment;
    }

    private void Known(params ShiftDefinition[] definitions)
    {
        foreach (var definition in definitions)
        {
            _definitions.GetByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(definition);
        }

        _definitions.ListAsync(false, Arg.Any<CancellationToken>()).Returns(definitions.Where(d => d.IsActive).ToList());
    }

    [Fact]
    public async Task No_assignment_falls_back_to_work_schedule_day()
    {
        var plan = await _resolver.ResolveAsync(_employeeId, Day, null, TestContext.Current.CancellationToken);

        plan.Start.ShouldBe(new TimeOnly(8, 0));
        plan.End.ShouldBe(new TimeOnly(17, 0));
        plan.BreakMinutes.ShouldBe(60);
        plan.PlannedMinutes.ShouldBe(480);
        plan.Rules.ShouldBeNull();
    }

    [Fact]
    public async Task Assignment_without_definition_takes_priority_over_work_schedule()
    {
        Assign(new TimeOnly(14, 0), new TimeOnly(22, 0), 30);

        var plan = await _resolver.ResolveAsync(_employeeId, Day, null, TestContext.Current.CancellationToken);

        plan.Start.ShouldBe(new TimeOnly(14, 0));
        plan.End.ShouldBe(new TimeOnly(22, 0));
        plan.BreakMinutes.ShouldBe(30);
        plan.PlannedMinutes.ShouldBe(450);
        plan.Rules.ShouldBeNull();
        await _schedules.DidNotReceiveWithAnyArgs().GetForDateAsync(default, default, default);
    }

    [Fact]
    public async Task Assignment_with_definition_uses_definition_hours_and_rules()
    {
        var definition = ShiftDefinition.Create(Tenant, FixedValues("SAB", Minute(7), Minute(15)) with { LateToleranceMinutes = 10 }).Value;
        Known(definition);
        Assign(new TimeOnly(9, 0), new TimeOnly(18, 0), 60, definition.Id);

        var plan = await _resolver.ResolveAsync(_employeeId, Day, null, TestContext.Current.CancellationToken);

        plan.Start.ShouldBe(new TimeOnly(7, 0));
        plan.End.ShouldBe(new TimeOnly(15, 0));
        plan.ShiftDefinitionId.ShouldBe(definition.Id);
        plan.Rules.ShouldNotBeNull().LateToleranceMinutes.ShouldBe(10);
    }

    [Fact]
    public async Task Dynamic_assignment_resolves_to_definition_closest_to_first_in()
    {
        var morning = Dynamic("D06", Minute(6), Minute(14));
        var afternoon = Dynamic("D14", Minute(14), Minute(22));
        Known(morning, afternoon);
        Assign(new TimeOnly(6, 0), new TimeOnly(14, 0), 0, morning.Id);

        var plan = await _resolver.ResolveAsync(_employeeId, Day, new TimeOnly(13, 40), TestContext.Current.CancellationToken);

        plan.ShiftDefinitionId.ShouldBe(afternoon.Id);
        plan.Start.ShouldBe(new TimeOnly(14, 0));
        plan.IsAmbiguous.ShouldBeFalse();
    }

    [Fact]
    public async Task Dynamic_assignment_tie_marks_plan_ambiguous()
    {
        var early = Dynamic("D06", Minute(6), Minute(14));
        var late = Dynamic("D10", Minute(10), Minute(18));
        Known(early, late);
        Assign(new TimeOnly(6, 0), new TimeOnly(14, 0), 0, early.Id);

        var plan = await _resolver.ResolveAsync(_employeeId, Day, new TimeOnly(8, 0), TestContext.Current.CancellationToken);

        plan.IsAmbiguous.ShouldBeTrue();
    }

    [Fact]
    public async Task Assignment_with_rest_day_type_ignores_hours()
    {
        Assign(new TimeOnly(9, 0), new TimeOnly(18, 0), 60, dayType: ShiftDayType.WeeklyRest);

        var plan = await _resolver.ResolveAsync(_employeeId, Day, null, TestContext.Current.CancellationToken);
        plan.IsWorkingDay.ShouldBeFalse();
        plan.DayType.ShouldBe(ShiftDayType.WeeklyRest);
        plan.PlannedMinutes.ShouldBe(0);

        // Okutma (elle kayıt) varsa gün plansız çalışma sayılır.
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Tenant);
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var days = Substitute.For<IAttendanceDayRepository>();
        AttendanceDay? recorded = null;
        days.Add(Arg.Do<AttendanceDay>(d => recorded = d));

        var result = await new RecordAttendanceHandler(days, _resolver, tenant, dataScope, new FixedLocalClock(new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc)), new InMemoryPunchRepository(), Substitute.For<ICurrentUser>(), Substitute.For<IMonthlyTimesheetApprovalRepository>(), Substitute.For<IAttendanceUnitOfWork>())
            .Handle(new RecordAttendanceCommand(_employeeId, Day, new TimeOnly(9, 0), new TimeOnly(18, 0)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        recorded.ShouldNotBeNull().Anomalies.ShouldBe(AttendanceAnomaly.UnplannedWork);
        recorded.PlannedMinutes.ShouldBe(0);
    }

    [Fact]
    public async Task Recorded_day_applies_definition_tolerance()
    {
        var definition = ShiftDefinition.Create(Tenant, FixedValues("SAB", Minute(9), Minute(18)) with { LateToleranceMinutes = 10 }).Value;
        Known(definition);
        Assign(new TimeOnly(9, 0), new TimeOnly(18, 0), 0, definition.Id);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Tenant);
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var days = Substitute.For<IAttendanceDayRepository>();
        AttendanceDay? recorded = null;
        days.Add(Arg.Do<AttendanceDay>(d => recorded = d));

        await new RecordAttendanceHandler(days, _resolver, tenant, dataScope, new FixedLocalClock(new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc)), new InMemoryPunchRepository(), Substitute.For<ICurrentUser>(), Substitute.For<IMonthlyTimesheetApprovalRepository>(), Substitute.For<IAttendanceUnitOfWork>())
            .Handle(new RecordAttendanceCommand(_employeeId, Day, new TimeOnly(9, 8), new TimeOnly(18, 0)), TestContext.Current.CancellationToken);

        recorded.ShouldNotBeNull().Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse();
    }
}
