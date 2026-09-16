using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Application.Goals;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>
/// G-8-19/G-8-20: bir hedef bir değerlendirme dönemine bağlıysa (ReviewCycleId dolu), dönem kapalıyken hiçbir
/// değişiklik kabul edilmez ve hedef tarihi dönem aralığı dışında oluşturulamaz - bkz. GoalCommands.cs'teki
/// GoalReviewCycleGuard XML doc'u. Karar (b): öz-servis kontrol sırası önce sahiplik (403), sonra kapalı dönem (409).
/// </summary>
public sealed class GoalCommandsTests
{
    private static ReviewCycle OpenCycle(DateOnly start, DateOnly end)
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", start, end).Value;
        cycle.Activate();
        return cycle;
    }

    private static ReviewCycle ClosedCycle(DateOnly start, DateOnly end)
    {
        var cycle = OpenCycle(start, end);
        cycle.Close();
        return cycle;
    }

    [Fact]
    public async Task UpdateGoalProgress_CycleClosed_IsRejected()
    {
        var cycle = ClosedCycle(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var goal = Goal.Create(cycle.TenantId, Guid.NewGuid(), "Satışları artır", null, new DateOnly(2026, 2, 1), cycle.Id).Value;

        var goals = Substitute.For<IGoalRepository>();
        goals.GetByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(goal);
        var cycles = Substitute.For<IReviewCycleRepository>();
        cycles.GetByIdAsync(cycle.Id, Arg.Any<CancellationToken>()).Returns(cycle);
        var handler = new UpdateGoalProgressHandler(goals, cycles);

        var result = await handler.Handle(new UpdateGoalProgressCommand(goal.Id, 50), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceErrors.ReviewImmutable);
        goal.Progress.ShouldBe(0);
    }

    [Fact]
    public async Task CancelGoal_CycleClosed_IsRejected()
    {
        var cycle = ClosedCycle(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var goal = Goal.Create(cycle.TenantId, Guid.NewGuid(), "Satışları artır", null, new DateOnly(2026, 2, 1), cycle.Id).Value;

        var goals = Substitute.For<IGoalRepository>();
        goals.GetByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(goal);
        var cycles = Substitute.For<IReviewCycleRepository>();
        cycles.GetByIdAsync(cycle.Id, Arg.Any<CancellationToken>()).Returns(cycle);
        var handler = new CancelGoalHandler(goals, cycles);

        var result = await handler.Handle(new CancelGoalCommand(goal.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceErrors.ReviewImmutable);
        goal.Status.ShouldBe(GoalStatus.Open);
    }

    [Fact]
    public async Task UpdateMyGoalProgress_NotOwner_ReturnsForbidden_EvenIfCycleClosed()
    {
        var cycle = ClosedCycle(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var goal = Goal.Create(cycle.TenantId, Guid.NewGuid(), "Satışları artır", null, new DateOnly(2026, 2, 1), cycle.Id).Value;

        var goals = Substitute.For<IGoalRepository>();
        goals.GetByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(goal);
        var cycles = Substitute.For<IReviewCycleRepository>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(Guid.NewGuid()); // not the goal's owner
        var handler = new UpdateMyGoalProgressHandler(currentUser, goals, cycles);

        var result = await handler.Handle(new UpdateMyGoalProgressCommand(goal.Id, 50), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        await cycles.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task CreateGoal_TargetDateOutsideCycleRange_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var cycle = OpenCycle(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        var employees = Substitute.For<IEmployeeLookup>();
        employees.ExistsActiveAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var goals = Substitute.For<IGoalRepository>();
        var cycles = Substitute.For<IReviewCycleRepository>();
        cycles.GetByIdAsync(cycle.Id, Arg.Any<CancellationToken>()).Returns(cycle);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var handler = new CreateGoalHandler(tenant, employees, goals, cycles);

        var result = await handler.Handle(
            new CreateGoalCommand(employeeId, "Satışları artır", null, new DateOnly(2026, 4, 1), cycle.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceErrors.GoalTargetDateOutsideCycle);
        goals.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task CreateMyGoal_NoReviewCycle_BehaviorUnchanged()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var goals = Substitute.For<IGoalRepository>();
        var cycles = Substitute.For<IReviewCycleRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var handler = new CreateMyGoalHandler(tenant, currentUser, goals, cycles);

        var result = await handler.Handle(
            new CreateMyGoalCommand("Satışları artır", null, new DateOnly(2026, 12, 31), null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        goals.Received(1).Add(Arg.Any<Goal>());
        await cycles.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }
}
