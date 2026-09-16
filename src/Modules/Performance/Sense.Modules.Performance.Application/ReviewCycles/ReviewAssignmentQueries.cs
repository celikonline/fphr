using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

/// <summary>"Bana atanan formlar" (Uygulamalar → Performans self-servis).</summary>
[RequiresPermission("performance.my.assignments")]
public sealed record ListMyAssignmentsQuery : IQuery<IReadOnlyList<MyAssignmentDto>>;

public sealed class ListMyAssignmentsHandler(ICurrentUser currentUser, IReviewCycleRepository cycles)
    : IQueryHandler<ListMyAssignmentsQuery, IReadOnlyList<MyAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<MyAssignmentDto>>> Handle(ListMyAssignmentsQuery query, CancellationToken cancellationToken)
    {
        var employeeId = currentUser.EmployeeId ?? Guid.Empty;
        var list = await cycles.ListWithAssignmentsForEvaluatorAsync(employeeId, cancellationToken);

        IReadOnlyList<MyAssignmentDto> dtos = list
            .SelectMany(cycle => cycle.Participants
                .SelectMany(p => p.Assignments.Where(a => a.EvaluatorEmployeeId == employeeId))
                .Select(a => new MyAssignmentDto(
                    cycle.Id, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd,
                    a.Id, a.Relation, FindParticipant(cycle, a.Id), a.Status)))
            .ToList();

        return Result.Success(dtos);
    }

    private static Guid FindParticipant(ReviewCycle cycle, Guid assignmentId) =>
        cycle.Participants.First(p => p.Assignments.Any(a => a.Id == assignmentId)).EmployeeId;
}

/// <summary>"Hedeflerim": katılımcının IncludeGoals açık olduğu Yetkinlik + Hedef dönemlerindeki hedefleri.</summary>
[RequiresPermission("performance.my.assignments")]
public sealed record ListMyGoalsQuery : IQuery<IReadOnlyList<MyGoalCycleDto>>;

public sealed record MyGoalDto(Guid ParticipantId, Guid GoalId, string Name, string? Description, decimal TargetValue, decimal? ActualValue, decimal? Points, GoalStatus Status);

public sealed record MyGoalCycleDto(Guid ReviewCycleId, string ReviewCycleName, DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<MyGoalDto> Goals);

public sealed class ListMyGoalsHandler(ICurrentUser currentUser, IReviewCycleRepository cycles) : IQueryHandler<ListMyGoalsQuery, IReadOnlyList<MyGoalCycleDto>>
{
    public async Task<Result<IReadOnlyList<MyGoalCycleDto>>> Handle(ListMyGoalsQuery query, CancellationToken cancellationToken)
    {
        var employeeId = currentUser.EmployeeId ?? Guid.Empty;
        var list = await cycles.ListWithGoalsForParticipantAsync(employeeId, cancellationToken);

        IReadOnlyList<MyGoalCycleDto> dtos = list
            .Select(cycle =>
            {
                var participant = cycle.Participants.First(p => p.EmployeeId == employeeId && p.IncludeGoals);
                var goals = participant.Goals
                    .Select(g => new MyGoalDto(participant.Id, g.Id, g.Name, g.Description, g.TargetValue, g.ActualValue, g.Points, g.Status))
                    .ToList();
                return new MyGoalCycleDto(cycle.Id, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd, goals);
            })
            .ToList();

        return Result.Success(dtos);
    }
}
