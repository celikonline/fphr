using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain;

/// <summary>ReviewCycle aggregate repository.</summary>
public interface IReviewCycleRepository
{
    void Add(ReviewCycle cycle);

    /// <summary>Loads the full aggregate (competencies/questions/relation configs/participants/assignments/responses).</summary>
    Task<Result<ReviewCycle>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewCycle>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads only the cycles (with participants/assignments) where the employee has at least one assignment - "Bana atanan formlar".</summary>
    Task<IReadOnlyList<ReviewCycle>> ListWithAssignmentsForEvaluatorAsync(Guid evaluatorEmployeeId, CancellationToken cancellationToken = default);

    /// <summary>Loads only the cycles (with participants/goals) where the employee is a participant with goals - "Hedeflerim".</summary>
    Task<IReadOnlyList<ReviewCycle>> ListWithGoalsForParticipantAsync(Guid participantEmployeeId, CancellationToken cancellationToken = default);
}

/// <summary>Kiracı tanımlı hedef kategorileri (basit liste, kendi aggregate'i).</summary>
public interface IGoalCategoryRepository
{
    void Add(GoalCategory category);

    Task<Result<GoalCategory>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GoalCategory>> ListAsync(CancellationToken cancellationToken = default);
}
