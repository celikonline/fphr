using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.Applications;

/// <summary>
/// Fixed pipeline stage set for R0 (roadmap #90's full custom-pipeline-per-posting, rule engine and form-per-stage
/// gating are R1/R2). <see cref="Hired"/> and <see cref="Rejected"/> are terminal.
/// </summary>
public enum ApplicationStage
{
    Applied,
    Screening,
    Interview,
    Offer,
    Hired,
    Rejected,
}

/// <summary>A candidate's application to one job posting (Kolay İK "Başvuru"). One (JobPostingId, CandidateId) pair should be applied to once - enforced by a unique index, not here.</summary>
public sealed class JobApplication : TenantAggregateRoot<Guid>, IAuditLogged
{
    private JobApplication()
    {
    }

    private JobApplication(Guid id, Guid tenantId, Guid jobPostingId, Guid candidateId)
        : base(id, tenantId)
    {
        JobPostingId = jobPostingId;
        CandidateId = candidateId;
        Stage = ApplicationStage.Applied;
        AppliedAt = DateTime.UtcNow;
    }

    public Guid JobPostingId { get; private set; }

    public Guid CandidateId { get; private set; }

    public ApplicationStage Stage { get; private set; }

    public DateTime AppliedAt { get; private set; }

    public DateTime? ArchivedAt { get; private set; }

    public static JobApplication Create(Guid tenantId, Guid jobPostingId, Guid candidateId, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), tenantId, jobPostingId, candidateId);

    /// <summary>Manual pipeline move (roadmap flow #4's rule-engine side effects on transition are deferred to R2).</summary>
    public Result MoveStage(ApplicationStage stage)
    {
        if (IsTerminal())
        {
            return Error.Rule(RecruitmentDomainErrors.ApplicationAlreadyTerminal);
        }

        Stage = stage;
        if (stage is ApplicationStage.Hired or ApplicationStage.Rejected)
        {
            ArchivedAt = DateTime.UtcNow;
        }

        return Result.Success();
    }

    private bool IsTerminal() => Stage is ApplicationStage.Hired or ApplicationStage.Rejected;
}
