using Sense.Modules.Performance.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.Goals;

public sealed record GoalDto(Guid Id, Guid EmployeeId, string Title, string? Description, DateOnly TargetDate, int Progress, GoalStatus Status, Guid? ReviewCycleId);

/// <summary>The caller's own goals.</summary>
[RequiresPermission("performance.my.goals")]
public sealed record ListMyGoalsQuery : IQuery<IReadOnlyList<GoalDto>>;

public sealed class ListMyGoalsHandler(ICurrentUser currentUser, IGoalRepository goals) : IQueryHandler<ListMyGoalsQuery, IReadOnlyList<GoalDto>>
{
    public async Task<Result<IReadOnlyList<GoalDto>>> Handle(ListMyGoalsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Result.Success<IReadOnlyList<GoalDto>>([]);
        }

        var list = await goals.ListByEmployeeAsync(employeeId, cancellationToken);
        IReadOnlyList<GoalDto> dtos = list.Select(GoalMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>HR/manager: a specific employee's goals.</summary>
[RequiresPermission("performance.goals.view")]
public sealed record ListGoalsByEmployeeQuery(Guid EmployeeId) : IQuery<IReadOnlyList<GoalDto>>;

public sealed class ListGoalsByEmployeeHandler(IGoalRepository goals) : IQueryHandler<ListGoalsByEmployeeQuery, IReadOnlyList<GoalDto>>
{
    public async Task<Result<IReadOnlyList<GoalDto>>> Handle(ListGoalsByEmployeeQuery query, CancellationToken cancellationToken)
    {
        var list = await goals.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        IReadOnlyList<GoalDto> dtos = list.Select(GoalMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Manager's team goals: employees within the caller's "performance.team.goals" data scope (mirrors Leave's ListTeamLeaveRequestsQuery pattern).</summary>
[RequiresPermission("performance.team.goals")]
public sealed record ListTeamGoalsQuery : IQuery<IReadOnlyList<GoalDto>>;

public sealed class ListTeamGoalsHandler(IDataScopeService dataScope, IGoalRepository goals) : IQueryHandler<ListTeamGoalsQuery, IReadOnlyList<GoalDto>>
{
    public async Task<Result<IReadOnlyList<GoalDto>>> Handle(ListTeamGoalsQuery query, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(PerformancePermissions.TeamGoals.Key, cancellationToken);
        if (visible is { Count: 0 })
        {
            return Result.Success<IReadOnlyList<GoalDto>>([]);
        }

        var list = await goals.ListByEmployeeIdsAsync(visible, cancellationToken);
        IReadOnlyList<GoalDto> dtos = list.Select(GoalMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class GoalMapper
{
    public static GoalDto ToDto(Goal goal) => new(goal.Id, goal.EmployeeId, goal.Title, goal.Description, goal.TargetDate, goal.Progress, goal.Status, goal.ReviewCycleId);
}
