using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.Goals;

/// <summary><paramref name="EmployeeName"/> is only populated for the "team" view (see <see cref="ListTeamGoalsHandler"/>);
/// null on "me"/"employee" views where the caller already knows whose goal it is.</summary>
public sealed record GoalDto(Guid Id, Guid EmployeeId, string Title, string? Description, DateOnly TargetDate, int Progress, GoalStatus Status, Guid? ReviewCycleId, string? EmployeeName = null);

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
        IReadOnlyList<GoalDto> dtos = list.Select(g => GoalMapper.ToDto(g)).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>HR/manager: a specific employee's goals.</summary>
[RequiresPermission("performance.goals.view")]
public sealed record ListGoalsByEmployeeQuery(Guid EmployeeId) : IQuery<IReadOnlyList<GoalDto>>;

public sealed class ListGoalsByEmployeeHandler(IGoalRepository goals, IDataScopeService dataScope)
    : IQueryHandler<ListGoalsByEmployeeQuery, IReadOnlyList<GoalDto>>
{
    public async Task<Result<IReadOnlyList<GoalDto>>> Handle(ListGoalsByEmployeeQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(PerformancePermissions.GoalsView.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(PerformanceErrors.EmployeeNotInScope);
        }

        var list = await goals.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        IReadOnlyList<GoalDto> dtos = list.Select(g => GoalMapper.ToDto(g)).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Manager's team goals: employees within the caller's "performance.team.goals" data scope (mirrors Leave's ListTeamLeaveRequestsQuery pattern).</summary>
[RequiresPermission("performance.team.goals")]
public sealed record ListTeamGoalsQuery : IQuery<IReadOnlyList<GoalDto>>;

/// <summary>Also resolves <see cref="GoalDto.EmployeeName"/> via <see cref="IEmployeeDirectory"/> (cross-module read,
/// no data-scope re-check needed - the goal set is already scope-filtered above) so the team list doesn't force the
/// caller into a second round-trip to Employee just to show whose goal it is.</summary>
public sealed class ListTeamGoalsHandler(IDataScopeService dataScope, IGoalRepository goals, ITenantContext tenant, IEmployeeDirectory employees)
    : IQueryHandler<ListTeamGoalsQuery, IReadOnlyList<GoalDto>>
{
    public async Task<Result<IReadOnlyList<GoalDto>>> Handle(ListTeamGoalsQuery query, CancellationToken cancellationToken)
    {
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(PerformancePermissions.TeamGoals.Key, cancellationToken);
        if (visible is { Count: 0 })
        {
            return Result.Success<IReadOnlyList<GoalDto>>([]);
        }

        var list = await goals.ListByEmployeeIdsAsync(visible, cancellationToken);
        if (list.Count == 0)
        {
            return Result.Success<IReadOnlyList<GoalDto>>([]);
        }

        var employeeIds = list.Select(g => g.EmployeeId).Distinct().ToList();
        var names = (await employees.GetContactsAsync(tenant.TenantId, employeeIds, cancellationToken))
            .ToDictionary(c => c.EmployeeId, c => $"{c.FirstName} {c.LastName}".Trim());

        IReadOnlyList<GoalDto> dtos = list.Select(g => GoalMapper.ToDto(g, names.GetValueOrDefault(g.EmployeeId))).ToList();
        return Result.Success(dtos);
    }
}

internal static class GoalMapper
{
    public static GoalDto ToDto(Goal goal, string? employeeName = null) =>
        new(goal.Id, goal.EmployeeId, goal.Title, goal.Description, goal.TargetDate, goal.Progress, goal.Status, goal.ReviewCycleId, employeeName);
}
