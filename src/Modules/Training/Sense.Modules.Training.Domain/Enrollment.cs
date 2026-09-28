using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Domain;

/// <summary>
/// Eğitim katılımcısı kaydı.
/// </summary>
public sealed class Enrollment : TenantAggregateRoot<Guid>, IAuditLogged
{
    // ReSharper disable UnusedMember.Local
    private Enrollment() { }

    public Guid TrainingProgramId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public DateTime EnrolledAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? CertificateNumber { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public static Result<Enrollment> Create(
        Guid trainingProgramId,
        Guid tenantId,
        Guid employeeId)
    {
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrainingProgramId = trainingProgramId,
            EmployeeId = employeeId,
            Status = EnrollmentStatus.Enrolled,
            EnrolledAt = DateTime.UtcNow
        };

        return enrollment;
    }

    /// <summary>
    /// Katılımcıyı tamamlanan durumuna al.
    /// </summary>
    public Result Complete(string? certificateNumber = null)
    {
        if (Status == EnrollmentStatus.Completed)
            return TrainingDomainErrors.AlreadyCompleted;

        Status = EnrollmentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        CertificateNumber = certificateNumber;

        return Result.Success();
    }

    /// <summary>
    /// Katılımcıyı iptal edildi durumuna al. Kayıt silinmez; iptal tarihi ve (varsa) nedeni saklanır
    /// (roadmap #72 — iptal geçmişi tutulsun).
    /// </summary>
    public Result Cancel(string? reason = null)
    {
        if (Status == EnrollmentStatus.Cancelled)
            return TrainingDomainErrors.AlreadyCancelled;

        Status = EnrollmentStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;

        return Result.Success();
    }
}
