namespace Sense.Modules.Training.Contracts;

public sealed record EnrollmentDto(
    Guid Id,
    Guid TrainingProgramId,
    Guid EmployeeId,
    string Status,
    DateTime EnrolledAt,
    DateTime? CompletedAt,
    string? CertificateNumber);
