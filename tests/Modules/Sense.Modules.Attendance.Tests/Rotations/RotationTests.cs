using NSubstitute;
using Sense.Modules.Attendance.Application.Rotations;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Rotations;

public sealed class RotationExpanderTests
{
    private static readonly Guid Day = Guid.NewGuid();
    private static readonly Guid Evening = Guid.NewGuid();
    private static readonly Guid Night = Guid.NewGuid();
    private static readonly Guid Fourth = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 9, 1);

    private static RotationPattern Pattern(RotationPresetDefinition def) =>
        RotationPattern.Create(Guid.NewGuid(), "P", "Pattern", def.Preset, def.CycleLengthDays, def.Steps).Value;

    [Fact]
    public void TwoByTwelve_alternates_day_night_rest_rest()
    {
        var days = RotationExpander.Expand(Pattern(RotationPresets.TwoByTwelve(Day, Night)), Start, 0, Start, Start.AddDays(4));

        days.Select(d => (d.ShiftDefinitionId, d.DayType)).ToList().ShouldBe(
        [
            ((Guid?)Day, ShiftDayType.Work), (Night, ShiftDayType.Work), (null, ShiftDayType.RestDay), (null, ShiftDayType.RestDay), (Day, ShiftDayType.Work),
        ]);
    }

    [Fact]
    public void ThreeByEight_cycle_is_morning_evening_night_rest()
    {
        var days = RotationExpander.Expand(Pattern(RotationPresets.ThreeByEight(Day, Evening, Night)), Start, 0, Start, Start.AddDays(3));

        days.Select(d => d.ShiftDefinitionId).ToList().ShouldBe([Day, Evening, Night, null]);
        days[3].DayType.ShouldBe(ShiftDayType.RestDay);
    }

    [Fact]
    public void FourBySix_cycle_length_is_5()
    {
        var def = RotationPresets.FourBySix(Day, Evening, Night, Fourth);

        def.CycleLengthDays.ShouldBe(5);
        var days = RotationExpander.Expand(Pattern(def), Start, 0, Start, Start.AddDays(5));
        days.Select(d => d.ShiftDefinitionId).ToList().ShouldBe([Day, Evening, Night, Fourth, null, Day]);
    }

    [Fact]
    public void Offset_shifts_cycle_start()
    {
        var days = RotationExpander.Expand(Pattern(RotationPresets.TwoByTwelve(Day, Night)), Start, 1, Start, Start.AddDays(1));

        days[0].ShiftDefinitionId.ShouldBe(Night);
        days[1].DayType.ShouldBe(ShiftDayType.RestDay);
    }

    [Fact]
    public void EndDate_stops_expansion()
    {
        var days = RotationExpander.Expand(Pattern(RotationPresets.TwoByTwelve(Day, Night)), Start, 0, Start, Start.AddDays(1));

        days.Count.ShouldBe(2);
        days[^1].Date.ShouldBe(Start.AddDays(1));
    }
}

public sealed class ApplyRotationHandlerTests
{
    private static readonly DateOnly Start = new(2026, 8, 30);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IRotationRepository _rotations = Substitute.For<IRotationRepository>();
    private readonly IShiftDefinitionRepository _definitions = Substitute.For<IShiftDefinitionRepository>();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IEmployeeLookup _lookup = Substitute.For<IEmployeeLookup>();
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();
    private readonly IDataScopeService _scope = Substitute.For<IDataScopeService>();
    private readonly ShiftDefinition _dayDef;
    private readonly ShiftDefinition _nightDef;
    private readonly RotationPattern _pattern;

    public ApplyRotationHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _lookup.ExistsActiveAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _dayDef = Definition("D", 8 * 60, 20 * 60);
        _nightDef = Definition("N", 20 * 60, 8 * 60);
        var def = RotationPresets.TwoByTwelve(_dayDef.Id, _nightDef.Id);
        _pattern = RotationPattern.Create(_tenantId, "2x12", "2x12", def.Preset, def.CycleLengthDays, def.Steps).Value;
        _rotations.GetPatternByIdAsync(_pattern.Id, Arg.Any<CancellationToken>()).Returns(_pattern);
        _rotations.ListAssignmentsOverlappingAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<RotationAssignment>());
        _definitions.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new List<ShiftDefinition> { _dayDef, _nightDef });
        _shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment>());
        _locks.FindByPeriodAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ShiftPlanLock?)null);
        _directory.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeId, "Ayşe", "Yılmaz", null, null, null, "active") });
    }

    private ShiftDefinition Definition(string code, int start, int end) =>
        ShiftDefinition.Create(_tenantId, new ShiftDefinitionValues(code, code, "#112233", ShiftType.Fixed, start, end, false, null, null, null, [], 0, 0, null, null, null, null, 0)).Value;

    private ApplyRotationHandler CreateHandler() => new(_tenant, _rotations, _definitions, _shifts, _locks, _lookup, _directory, _scope);

    [Fact]
    public async Task Apply_creates_draft_assignments_skipping_locked_months()
    {
        // 30 Ağu - 2 Eyl: Ağustos kilitli (2 gün atlanır), Eylül'de 2 gün üretilir.
        _locks.FindByPeriodAsync(new DateOnly(2026, 8, 1), Arg.Any<CancellationToken>())
            .Returns(ShiftPlanLock.Lock(_tenantId, new DateOnly(2026, 8, 1), DateTime.UtcNow, Guid.NewGuid(), ShiftPlanLockSource.Manual));

        var result = await CreateHandler().Handle(
            new ApplyRotationCommand(_pattern.Id, [_employeeId], Start, Start.AddDays(3), 0), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedCount.ShouldBe(2);
        result.Value.SkippedLockedMonths.ShouldBe(["2026-08"]);
        _shifts.Received(2).Add(Arg.Is<ShiftAssignment>(a => a.Status == ShiftAssignmentStatus.Draft && a.Date.Month == 9 && a.DayType == ShiftDayType.RestDay));
        _rotations.Received(1).AddAssignment(Arg.Any<RotationAssignment>());
    }

    [Fact]
    public async Task Apply_creates_work_days_from_shift_definition_times()
    {
        var result = await CreateHandler().Handle(
            new ApplyRotationCommand(_pattern.Id, [_employeeId], Start, Start.AddDays(1), 0), TestContext.Current.CancellationToken);

        result.Value.SkippedLockedMonths.ShouldBeEmpty();
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.ShiftDefinitionId == _dayDef.Id && a.Start == new TimeOnly(8, 0) && !a.IsNight));
        _shifts.Received(1).Add(Arg.Is<ShiftAssignment>(a => a.ShiftDefinitionId == _nightDef.Id && a.IsNight));
    }

    [Fact]
    public async Task Overlapping_assignments_for_employee_rejected()
    {
        var existing = RotationAssignment.Create(_tenantId, _employeeId, _pattern.Id, Start, null, 0).Value;
        _rotations.ListAssignmentsOverlappingAsync(Start, Start.AddDays(3), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<RotationAssignment> { existing });

        var result = await CreateHandler().Handle(
            new ApplyRotationCommand(_pattern.Id, [_employeeId], Start, Start.AddDays(3), 0), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RotationErrors.AssignmentOverlap);
        result.Error.Args!["employee"].ShouldBe("Ayşe Yılmaz");
        _rotations.DidNotReceive().AddAssignment(Arg.Any<RotationAssignment>());
        _shifts.DidNotReceive().Add(Arg.Any<ShiftAssignment>());
    }
}
