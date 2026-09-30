using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.ShiftDefinitions;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.ShiftDefinitions.ShiftDefinitionFixtures;

namespace Sense.Modules.Attendance.Tests.ShiftDefinitions;

/// <summary>Vardiya kataloğu CRUD + "Kaydet &amp; Geçmişe Uygula" (Z0.2, spec §5.3).</summary>
public sealed class ShiftDefinitionFeaturesTests
{
    private static readonly DateTime NowLocal = new(2026, 9, 25, 10, 0, 0);

    private readonly IShiftDefinitionRepository _definitions = Substitute.For<IShiftDefinitionRepository>();
    private readonly IAttendanceDayRecalculator _recalculator = Substitute.For<IAttendanceDayRecalculator>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly Guid _jobId = Guid.NewGuid();

    public ShiftDefinitionFeaturesTests()
    {
        _tenant.TenantId.Returns(Tenant);
        _recalculator.EnqueueAsync(default, default, default, default, default).ReturnsForAnyArgs(_jobId);
    }

    private static ShiftDefinitionInput Input(string code = "SAB", int lateTolerance = 0) => new(
        code, "Sabah", "#336699", ShiftType.Fixed, Minute(9), Minute(18), false, null, null, null,
        [new ShiftBreakDto(Minute(12), 60, false)], lateTolerance, 0, null, null, 15, 60, 0);

    private UpdateShiftDefinitionHandler UpdateHandler() =>
        new(_definitions, _recalculator, _tenant, new FixedLocalClock(NowLocal, NowLocal));

    private ShiftDefinition Existing()
    {
        var definition = Fixed("SAB", Minute(9), Minute(18));
        _definitions.GetByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(definition);
        return definition;
    }

    private static ShiftAssignment AssignmentOn(Guid employeeId, DateOnly date, Guid definitionId)
    {
        var assignment = ShiftAssignment.Create(Tenant, employeeId, date, new TimeOnly(9, 0), new TimeOnly(18, 0), 60, false, null).Value;
        assignment.SetPlan(definitionId, ShiftDayType.Work);
        return assignment;
    }

    [Fact]
    public async Task Save_and_apply_to_past_enqueues_recalculation_for_affected_range()
    {
        var definition = Existing();
        var ayse = Guid.NewGuid();
        var mehmet = Guid.NewGuid();
        var applyFrom = new DateOnly(2026, 9, 1);
        var today = DateOnly.FromDateTime(NowLocal);
        _definitions.ListAssignmentsUsingAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { definition.Id })), applyFrom, today, Arg.Any<CancellationToken>())
            .Returns([
                AssignmentOn(ayse, new DateOnly(2026, 9, 3), definition.Id),
                AssignmentOn(mehmet, new DateOnly(2026, 9, 10), definition.Id),
                AssignmentOn(ayse, new DateOnly(2026, 9, 17), definition.Id),
            ]);

        var result = await UpdateHandler().Handle(
            new UpdateShiftDefinitionCommand(definition.Id, Input(lateTolerance: 10), ApplyToPast: true, ApplyFrom: applyFrom),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RecalculationJobId.ShouldBe(_jobId);
        result.Value.AffectedAssignmentCount.ShouldBe(3);
        definition.LateToleranceMinutes.ShouldBe(10);
        await _recalculator.Received(1).EnqueueAsync(
            Tenant, new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 17),
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(ayse) && ids.Contains(mehmet)),
            Arg.Any<CancellationToken>());
        definition.DomainEvents.OfType<ShiftDefinitionChanged>().ShouldHaveSingleItem().AppliedFrom.ShouldBe(new DateOnly(2026, 9, 3));
    }

    [Fact]
    public async Task Save_without_apply_to_past_does_not_enqueue()
    {
        var definition = Existing();

        var result = await UpdateHandler().Handle(
            new UpdateShiftDefinitionCommand(definition.Id, Input(), ApplyToPast: false, ApplyFrom: null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RecalculationJobId.ShouldBeNull();
        await _recalculator.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, default, default, default);
        definition.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Apply_to_past_without_affected_assignments_does_not_enqueue()
    {
        var definition = Existing();
        _definitions.ListAssignmentsUsingAsync(default!, default, default, default).ReturnsForAnyArgs(new List<ShiftAssignment>());

        var result = await UpdateHandler().Handle(
            new UpdateShiftDefinitionCommand(definition.Id, Input(), ApplyToPast: true, ApplyFrom: null), TestContext.Current.CancellationToken);

        result.Value.RecalculationJobId.ShouldBeNull();
        result.Value.AffectedAssignmentCount.ShouldBe(0);
        await _recalculator.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Apply_to_past_of_dynamic_definition_covers_all_dynamic_definitions()
    {
        var definition = Dynamic("D06", Minute(6), Minute(14));
        var sibling = Dynamic("D14", Minute(14), Minute(22));
        _definitions.GetByIdAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(definition);
        _definitions.ListAsync(true, Arg.Any<CancellationToken>()).Returns([definition, sibling, Fixed("SAB", Minute(9), Minute(18))]);
        _definitions.ListAssignmentsUsingAsync(default!, default, default, default).ReturnsForAnyArgs(new List<ShiftAssignment>());

        await UpdateHandler().Handle(
            new UpdateShiftDefinitionCommand(definition.Id, Input("D06") with { Type = ShiftType.Dynamic, StartMinute = Minute(6), EndMinute = Minute(14) }, true, null),
            TestContext.Current.CancellationToken);

        await _definitions.Received(1).ListAssignmentsUsingAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(definition.Id) && ids.Contains(sibling.Id)),
            DateOnly.MinValue, DateOnly.FromDateTime(NowLocal), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_from_in_future_is_rejected()
    {
        var definition = Existing();

        var result = await UpdateHandler().Handle(
            new UpdateShiftDefinitionCommand(definition.Id, Input(), true, DateOnly.FromDateTime(NowLocal).AddDays(1)), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.DateRangeInvalid);
    }

    [Fact]
    public async Task Update_rejects_code_used_by_another_definition()
    {
        var definition = Existing();
        _definitions.CodeExistsAsync("AKS", definition.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await UpdateHandler().Handle(
            new UpdateShiftDefinitionCommand(definition.Id, Input("AKS"), false, null), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.ShiftDefinitionCodeDuplicate);
        definition.Code.ShouldBe("SAB");
    }

    [Fact]
    public async Task Create_adds_definition_and_rejects_duplicate_code()
    {
        var handler = new CreateShiftDefinitionHandler(_definitions, _tenant);

        var created = await handler.Handle(new CreateShiftDefinitionCommand(Input()), TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue();
        _definitions.Received(1).Add(Arg.Is<ShiftDefinition>(d => d.Id == created.Value && d.Code == "SAB" && d.Breaks.Count == 1));

        _definitions.CodeExistsAsync("SAB", null, Arg.Any<CancellationToken>()).Returns(true);
        var duplicate = await handler.Handle(new CreateShiftDefinitionCommand(Input()), TestContext.Current.CancellationToken);
        duplicate.Error.Code.ShouldBe(AttendanceErrors.ShiftDefinitionCodeDuplicate);
    }

    [Fact]
    public async Task Create_rejects_fixed_break_without_start()
    {
        var input = Input() with { Breaks = [new ShiftBreakDto(null, 30, false)] };

        var result = await new CreateShiftDefinitionHandler(_definitions, _tenant).Handle(new CreateShiftDefinitionCommand(input), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionBreakInvalid);
    }

    [Fact]
    public async Task List_maps_rules_and_breaks()
    {
        var definition = ShiftDefinition.Create(Tenant, FixedValues("SAB", Minute(9), Minute(18)) with { Breaks = [ShiftBreak.Flexible(30)], BonusMinutes = 5 }).Value;
        _definitions.ListAsync(false, Arg.Any<CancellationToken>()).Returns([definition]);

        var result = await new ListShiftDefinitionsHandler(_definitions).Handle(new ListShiftDefinitionsQuery(), TestContext.Current.CancellationToken);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.Code.ShouldBe("SAB");
        dto.BonusMinutes.ShouldBe(5);
        dto.Breaks.ShouldHaveSingleItem().ShouldBe(new ShiftBreakDto(null, 30, true));
    }

    [Fact]
    public async Task Delete_of_definition_in_use_is_rejected_and_it_can_only_be_deactivated()
    {
        var definition = Existing();
        _definitions.IsInUseAsync(definition.Id, Arg.Any<CancellationToken>()).Returns(true);

        var deleted = await new DeleteShiftDefinitionHandler(_definitions).Handle(new DeleteShiftDefinitionCommand(definition.Id), TestContext.Current.CancellationToken);

        deleted.Error.Code.ShouldBe(AttendanceErrors.ShiftDefinitionInUse);
        _definitions.DidNotReceiveWithAnyArgs().Remove(default!);
        (await new SetShiftDefinitionActiveHandler(_definitions).Handle(new SetShiftDefinitionActiveCommand(definition.Id, false), TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        definition.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_of_unused_definition_removes_it()
    {
        var definition = Existing();

        var deleted = await new DeleteShiftDefinitionHandler(_definitions).Handle(new DeleteShiftDefinitionCommand(definition.Id), TestContext.Current.CancellationToken);

        deleted.IsSuccess.ShouldBeTrue();
        _definitions.Received(1).Remove(definition);
    }

    [Fact]
    public async Task Set_active_toggles_definition()
    {
        var definition = Existing();

        (await new SetShiftDefinitionActiveHandler(_definitions).Handle(new SetShiftDefinitionActiveCommand(definition.Id, false), TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();

        definition.IsActive.ShouldBeFalse();
    }
}
