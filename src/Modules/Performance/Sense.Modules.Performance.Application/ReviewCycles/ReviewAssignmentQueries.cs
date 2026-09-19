using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record AssignmentFormQuestionDto(Guid Id, int Order, string Text, CommentMode CommentModeUsed);

public sealed record AssignmentFormCompetencyDto(string Name, IReadOnlyList<AssignmentFormQuestionDto> Questions);

public sealed record AssignmentFormDto(
    Guid ReviewCycleId, string ReviewCycleName, Guid AssignmentId, EvaluatorRelation Relation, ReviewAssignmentStatus Status,
    RatingScale RatingScale, IReadOnlyList<AssignmentFormCompetencyDto> Competencies, IReadOnlyList<ReviewResponseDto> ExistingResponses);

/// <summary>Bir atamayı doldurmak için gereken minimum veri - GetReviewCycleQuery'nin aksine yalnız değerlendiricinin kendi atamasına özgü, performance.cycle.view gerektirmez.</summary>
[RequiresPermission("performance.form.fill")]
public sealed record GetAssignmentFormQuery(Guid ReviewCycleId, Guid AssignmentId, Guid ActorEmployeeId) : IQuery<AssignmentFormDto>;

public sealed class GetAssignmentFormHandler(IReviewCycleRepository cycles) : IQueryHandler<GetAssignmentFormQuery, AssignmentFormDto>
{
    public async Task<Result<AssignmentFormDto>> Handle(GetAssignmentFormQuery query, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(query.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var participant = cycle.Participants.FirstOrDefault(p => p.Assignments.Any(a => a.Id == query.AssignmentId));
        var assignment = participant?.Assignments.FirstOrDefault(a => a.Id == query.AssignmentId);
        if (participant is null || assignment is null)
        {
            return Error.NotFound(PerformanceDomainErrors.AssignmentNotFound);
        }

        if (assignment.EvaluatorEmployeeId != query.ActorEmployeeId)
        {
            return Error.Forbidden(PerformanceDomainErrors.AssignmentAssigneeMismatch);
        }

        var competencies = cycle.Competencies
            .Select(c => new AssignmentFormCompetencyDto(
                c.Name,
                c.Questions.OrderBy(q => q.Order)
                    .Select(q => new AssignmentFormQuestionDto(q.Id, q.Order, q.Text, q.CommentMode ?? cycle.DefaultCommentMode))
                    .ToList()))
            .ToList();

        var responses = assignment.Responses.Select(r => new ReviewResponseDto(r.QuestionId, r.Score, r.Comment)).ToList();
        return new AssignmentFormDto(cycle.Id, cycle.Name, assignment.Id, assignment.Relation, assignment.Status, cycle.RatingScale, competencies, responses);
    }
}

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

/// <summary>
/// "Sonuçlarım": her dönemdeki kendi katılımcı kaydım (ilerleme her zaman görünür; puan yalnız
/// ReviewCycle.EmployeeCanViewResults açıkken ve katılımcı Completed olunca gösterilir).
/// </summary>
[RequiresPermission("performance.my.assignments")]
public sealed record ListMyReviewResultsQuery : IQuery<IReadOnlyList<MyReviewResultDto>>;

public sealed record MyReviewResultDto(
    Guid ReviewCycleId, Guid ParticipantId, string ReviewCycleName, DateOnly PeriodStart, DateOnly PeriodEnd, ReviewKind Kind,
    ReviewParticipantStatus Status, decimal CompetencyProgress, decimal GoalProgress, bool CanViewResults, bool CanDownloadPdf, decimal? FinalScore);

public sealed class ListMyReviewResultsHandler(ICurrentUser currentUser, IReviewCycleRepository cycles)
    : IQueryHandler<ListMyReviewResultsQuery, IReadOnlyList<MyReviewResultDto>>
{
    public async Task<Result<IReadOnlyList<MyReviewResultDto>>> Handle(ListMyReviewResultsQuery query, CancellationToken cancellationToken)
    {
        var employeeId = currentUser.EmployeeId ?? Guid.Empty;
        var list = await cycles.ListWhereParticipantAsync(employeeId, cancellationToken);

        IReadOnlyList<MyReviewResultDto> dtos = list
            .Select(cycle =>
            {
                var participant = cycle.Participants.First(p => p.EmployeeId == employeeId);
                var canShowScore = cycle.EmployeeCanViewResults && participant.Status == ReviewParticipantStatus.Completed;
                return new MyReviewResultDto(
                    cycle.Id, participant.Id, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd, cycle.Kind,
                    participant.Status, participant.CompetencyProgress, participant.GoalProgress,
                    cycle.EmployeeCanViewResults, cycle.EmployeeCanDownloadPdf, canShowScore ? participant.FinalScore : null);
            })
            .OrderByDescending(dto => dto.PeriodStart)
            .ToList();

        return Result.Success(dtos);
    }
}
