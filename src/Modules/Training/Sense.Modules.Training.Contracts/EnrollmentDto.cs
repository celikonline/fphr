namespace Sense.Modules.Training.Contracts;

public sealed record EnrollmentDto(
    Guid Id,
    Guid TrainingProgramId,
    Guid EmployeeId,
    string Status,
    DateTime EnrolledAt,
    DateTime? CompletedAt,
    string? CertificateNumber);

/// <summary>"Eğitimlerim" satırı: çağıranın katılım kaydı + programın başlık/tarih/durum bilgisi.</summary>
public sealed record MyTrainingDto(
    Guid EnrollmentId,
    Guid TrainingProgramId,
    string Title,
    string? Description,
    string Category,
    DateTime StartDate,
    DateTime? EndDate,
    string ProgramStatus,
    string Status,
    DateTime EnrolledAt,
    DateTime? CompletedAt,
    string? CertificateNumber);
