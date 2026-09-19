using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Training.Application;

/// <summary>Training's SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface ITrainingUnitOfWork : IUnitOfWork;

/// <summary>Repository port for TrainingProgram aggregate root.</summary>
public interface ITrainingProgramRepository
{
    Task AddAsync(TrainingProgram program, CancellationToken cancellationToken = default);

    Task<TrainingProgram?> GetByIdAsync(Guid tenantId, Guid programId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingProgram>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task UpdateAsync(TrainingProgram program, CancellationToken cancellationToken = default);

    Task DeleteAsync(TrainingProgram program, CancellationToken cancellationToken = default);
}

/// <summary>Repository port for Enrollment aggregate root.</summary>
public interface IEnrollmentRepository
{
    Task AddAsync(Enrollment enrollment, CancellationToken cancellationToken = default);

    Task<Enrollment?> GetByIdAsync(Guid tenantId, Guid enrollmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrollment>> GetByTrainingProgramIdAsync(Guid tenantId, Guid programId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Enrollment>> GetByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    Task UpdateAsync(Enrollment enrollment, CancellationToken cancellationToken = default);

    Task DeleteAsync(Enrollment enrollment, CancellationToken cancellationToken = default);
}

/// <summary>Repository port for TrainingRequirement (roadmap #72: prerequisite training gating).</summary>
public interface ITrainingRequirementRepository
{
    Task AddAsync(TrainingRequirement requirement, CancellationToken cancellationToken = default);

    Task<TrainingRequirement?> GetByIdAsync(Guid tenantId, Guid requirementId, CancellationToken cancellationToken = default);

    /// <summary>All prerequisite rules that gate enrollment into <paramref name="programId"/>.</summary>
    Task<IReadOnlyList<TrainingRequirement>> GetByTrainingProgramIdAsync(Guid tenantId, Guid programId, CancellationToken cancellationToken = default);

    Task DeleteAsync(TrainingRequirement requirement, CancellationToken cancellationToken = default);
}
