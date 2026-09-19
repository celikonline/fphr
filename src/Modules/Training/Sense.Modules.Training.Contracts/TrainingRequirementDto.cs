namespace Sense.Modules.Training.Contracts;

/// <summary>Ön koşul eğitim kuralı (roadmap #72).</summary>
public sealed record TrainingRequirementDto(
    Guid Id,
    Guid TrainingProgramId,
    Guid PrerequisiteTrainingProgramId,
    bool BlocksAssignmentUntilCompleted);
