using Sense.Modules.Performance.Domain.Goals;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Modules.Performance.Domain.ReviewForms;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain;

/// <summary>ReviewCycle aggregate repository.</summary>
public interface IReviewCycleRepository
{
    void Add(ReviewCycle cycle);

    Task<Result<ReviewCycle>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewCycle>> ListAsync(ReviewCycleStatus? status = null, CancellationToken cancellationToken = default);
}

/// <summary>ReviewForm aggregate repository.</summary>
public interface IReviewFormRepository
{
    void Add(ReviewForm form);

    Task<Result<ReviewForm>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewForm>> ListByCycleAsync(Guid reviewCycleId, CancellationToken cancellationToken = default);
}

/// <summary>Goal (hedef/OKR) aggregate repository.</summary>
public interface IGoalRepository
{
    void Add(Goal goal);

    Task<Result<Goal>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Goal>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Null employeeIds = tenant-wide (caller has Company data scope); consumed by the "team goals" query
    /// with the caller's IDataScopeService-resolved visible employee set.</summary>
    Task<IReadOnlyList<Goal>> ListByEmployeeIdsAsync(IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);

    /// <summary>Open/InProgress goals for an employee - consumed by EmployeeTerminatedHandler to archive them.</summary>
    Task<IReadOnlyList<Goal>> ListOpenByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
