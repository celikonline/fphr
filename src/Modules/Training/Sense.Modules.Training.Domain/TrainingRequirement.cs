using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Training.Domain;

/// <summary>
/// Ön koşul eğitim kuralı (roadmap #72: "Ön koşul eğitim"): <see cref="TrainingProgramId"/> programına katılım,
/// <see cref="PrerequisiteTrainingProgramId"/> programının tamamlanmasına bağlanır. <see cref="BlocksAssignmentUntilCompleted"/>
/// true ise ön koşul tamamlanmadan katılımcı kaydı (Enrollment) reddedilir; false ise kural yalnız bilgilendirici kalır
/// (henüz bir tüketicisi yok - gating dışı kullanım ileriye dönük bırakıldı).
/// </summary>
public sealed class TrainingRequirement : TenantAggregateRoot<Guid>, IAuditLogged
{
    // ReSharper disable once UnusedMember.Local
    private TrainingRequirement() { }

    public Guid TrainingProgramId { get; private set; }

    public Guid PrerequisiteTrainingProgramId { get; private set; }

    public bool BlocksAssignmentUntilCompleted { get; private set; }

    public static Result<TrainingRequirement> Create(
        Guid tenantId,
        Guid trainingProgramId,
        Guid prerequisiteTrainingProgramId,
        bool blocksAssignmentUntilCompleted)
    {
        if (trainingProgramId == prerequisiteTrainingProgramId)
        {
            return TrainingDomainErrors.RequirementSelfReference;
        }

        return new TrainingRequirement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrainingProgramId = trainingProgramId,
            PrerequisiteTrainingProgramId = prerequisiteTrainingProgramId,
            BlocksAssignmentUntilCompleted = blocksAssignmentUntilCompleted,
        };
    }
}
