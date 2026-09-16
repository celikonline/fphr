using NSubstitute;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Application.Employees;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>EmployeeTerminated consumer: open/in-progress goals of the terminated employee freeze (Archived), see
/// EmployeeTerminatedHandler's XML doc for the KVKK/4857 md.18 rationale.</summary>
public sealed class EmployeeTerminatedHandlerTests
{
    private static readonly DateOnly TargetDate = new(2026, 12, 31);

    private static EmployeeTerminated Event(Guid employeeId) =>
        new(Guid.NewGuid(), employeeId, TerminationType.Resignation, "test", null);

    [Fact]
    public async Task Handle_OpenGoals_ArchivesAndSaves()
    {
        var employeeId = Guid.NewGuid();
        var goal = Goal.Create(Guid.NewGuid(), employeeId, "Satışları %10 artır", null, TargetDate, null).Value;
        var goals = Substitute.For<IGoalRepository>();
        goals.ListOpenByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns([goal]);
        var unitOfWork = Substitute.For<IPerformanceUnitOfWork>();
        var handler = new EmployeeTerminatedHandler(goals, unitOfWork);

        await handler.Handle(Event(employeeId), CancellationToken.None);

        goal.Status.ShouldBe(GoalStatus.Archived);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoOpenGoals_DoesNotSave()
    {
        var employeeId = Guid.NewGuid();
        var goals = Substitute.For<IGoalRepository>();
        goals.ListOpenByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns([]);
        var unitOfWork = Substitute.For<IPerformanceUnitOfWork>();
        var handler = new EmployeeTerminatedHandler(goals, unitOfWork);

        await handler.Handle(Event(employeeId), CancellationToken.None);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Redelivery_IsIdempotent()
    {
        // Second delivery finds the goal already Archived because ListOpenByEmployeeAsync (repository-level filter)
        // would no longer return it - simulated here by an already-archived goal reaching Archive() again.
        var employeeId = Guid.NewGuid();
        var goal = Goal.Create(Guid.NewGuid(), employeeId, "Satışları %10 artır", null, TargetDate, null).Value;
        goal.Archive();
        var goals = Substitute.For<IGoalRepository>();
        goals.ListOpenByEmployeeAsync(employeeId, Arg.Any<CancellationToken>()).Returns([goal]);
        var unitOfWork = Substitute.For<IPerformanceUnitOfWork>();
        var handler = new EmployeeTerminatedHandler(goals, unitOfWork);

        await handler.Handle(Event(employeeId), CancellationToken.None);

        goal.Status.ShouldBe(GoalStatus.Archived);
    }
}
