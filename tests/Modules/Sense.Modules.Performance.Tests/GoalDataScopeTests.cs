using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Application.Goals;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>Regresyon: yönetici hedef uçları yalnızca veri kapsamındaki çalışanlar için çalışır (ekip dışı çalışan → 403, varlık sızdırılmaz).</summary>
public sealed class GoalDataScopeTests
{
    private static readonly DateOnly Target = new(2026, 12, 31);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly IDataScopeService _scope = Substitute.For<IDataScopeService>();
    private readonly IGoalRepository _goals = Substitute.For<IGoalRepository>();
    private readonly IReviewCycleRepository _cycles = Substitute.For<IReviewCycleRepository>();

    public GoalDataScopeTests() =>
        _scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

    private Goal OutsiderGoal()
    {
        var goal = Goal.Create(_tenantId, _outsider, "Hedef", null, Target, null).Value;
        _goals.GetByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(goal));
        return goal;
    }

    private static void ShouldBeNotInScope(Result result) =>
        (result.IsFailure ? result.Error.Code : "success").ShouldBe(PerformanceErrors.EmployeeNotInScope);

    [Fact]
    public async Task ListGoalsByEmployee_OutOfScope_IsForbidden_AndReadsNothing()
    {
        var handler = new ListGoalsByEmployeeHandler(_goals, _scope);

        var result = await handler.Handle(new ListGoalsByEmployeeQuery(_outsider), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceErrors.EmployeeNotInScope);
        await _goals.DidNotReceive().ListByEmployeeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListGoalsByEmployee_InScope_ReturnsGoals()
    {
        _scope.CanAccessEmployeeAsync(Arg.Any<string>(), _outsider, Arg.Any<CancellationToken>()).Returns(true);
        _goals.ListByEmployeeAsync(_outsider, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Goal>)[]);

        var result = await new ListGoalsByEmployeeHandler(_goals, _scope).Handle(new ListGoalsByEmployeeQuery(_outsider), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateGoal_ForEmployeeOutOfScope_IsForbidden_EvenIfEmployeeDoesNotExist()
    {
        var employees = Substitute.For<IEmployeeLookup>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var handler = new CreateGoalHandler(tenant, employees, _goals, _cycles, _scope);

        var result = await handler.Handle(new CreateGoalCommand(Guid.NewGuid(), "Hedef", null, Target, null), CancellationToken.None);

        ShouldBeNotInScope(result);
        _goals.DidNotReceive().Add(Arg.Any<Goal>());
        await employees.DidNotReceive().ExistsActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProgress_OnGoalOutOfScope_IsForbidden_AndGoalUnchanged()
    {
        var goal = OutsiderGoal();

        var result = await new UpdateGoalProgressHandler(_goals, _cycles, _scope).Handle(new UpdateGoalProgressCommand(goal.Id, 90), CancellationToken.None);

        ShouldBeNotInScope(result);
        goal.Progress.ShouldBe(0);
    }

    [Fact]
    public async Task CancelGoal_OnGoalOutOfScope_IsForbidden_AndGoalStaysActive()
    {
        var goal = OutsiderGoal();

        var result = await new CancelGoalHandler(_goals, _cycles, _scope).Handle(new CancelGoalCommand(goal.Id), CancellationToken.None);

        ShouldBeNotInScope(result);
        goal.Status.ShouldNotBe(GoalStatus.Cancelled);
    }
}
