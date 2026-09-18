namespace Sense.Modules.Training.Contracts;

public sealed record TrainingProgramDto(
    Guid Id,
    string Title,
    string? Description,
    string Category,
    DateTime StartDate,
    DateTime? EndDate,
    int? MaxCapacity,
    string Status);
