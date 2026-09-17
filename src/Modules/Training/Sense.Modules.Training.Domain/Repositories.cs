using Sense.Shared.Kernel;

namespace Sense.Modules.Training.Domain;

/// <summary>
/// Eğitim programı repository.
/// </summary>
public interface ITrainingProgramRepository
{
    /// <summary>
    /// Program ekle.
    /// </summary>
    Task AddAsync(TrainingProgram program, CancellationToken ct = default);

    /// <summary>
    /// Program ID ile getir.
    /// </summary>
    Task<TrainingProgram?> GetByIdAsync(Guid tenantId, Guid programId, CancellationToken ct = default);

    /// <summary>
    /// Tüm programları getir (tenant tarafından filtrelenmesi repository'de).
    /// </summary>
    Task<IReadOnlyList<TrainingProgram>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Katılımcı kaydı repository.
/// </summary>
public interface IEnrollmentRepository
{
    /// <summary>
    /// Katılımcı kaydı ekle.
    /// </summary>
    Task AddAsync(Enrollment enrollment, CancellationToken ct = default);

    /// <summary>
    /// Katılımcı kaydı ID ile getir.
    /// </summary>
    Task<Enrollment?> GetByIdAsync(Guid tenantId, Guid enrollmentId, CancellationToken ct = default);

    /// <summary>
    /// Program'ın tüm katılımcılarını getir.
    /// </summary>
    Task<IReadOnlyList<Enrollment>> GetByProgramAsync(Guid tenantId, Guid programId, CancellationToken ct = default);

    /// <summary>
    /// Çalışan'ın tüm katılımlarını getir.
    /// </summary>
    Task<IReadOnlyList<Enrollment>> GetByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
