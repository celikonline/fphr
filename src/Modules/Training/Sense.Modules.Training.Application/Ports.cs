using Sense.Modules.Training.Domain;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Training.Application;

/// <summary>Training's SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface ITrainingUnitOfWork : IUnitOfWork;

/// <summary>Repository port for TrainingProgram aggregate root.</summary>
public interface ITrainingProgramRepository
{
    Task AddAsync(TrainingProgram program, CancellationToken cancellationToken = default);

    Task<TrainingProgram?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingProgram>> GetAllAsync(CancellationToken cancellationToken = default);

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
