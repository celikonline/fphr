using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.AutomationRules;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain;

/// <summary>JobPosting aggregate repository.</summary>
public interface IJobPostingRepository
{
    void Add(JobPosting posting);

    Task<Result<JobPosting>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobPosting>> ListAsync(JobPostingStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public career-site lookup by the permanent apply slug (R3): bypasses the tenant query filter, since the
    /// caller is anonymous and doesn't know the tenant yet - the caller must resolve tenant scope from the result
    /// before doing anything else (see PublicCareerCommands/Queries). <paramref name="asOf"/> mirrors
    /// <see cref="ListPublicByTenantAsync"/>'s ClosingDate rule (Faz 9 gün açılışı TALIMAT'ı madde 2) - a posting
    /// whose closing date has passed is treated exactly like one that doesn't exist (404 <c>PostingNotFound</c>,
    /// never a distinct "kapalı" signal, so a stale link can't be used to probe posting existence).
    /// </summary>
    Task<Result<JobPosting>> GetPublicByApplySlugAsync(string applySlug, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>Other Open, not-yet-expired postings of the same tenant (R3 "diğer açık pozisyonlar"), excluding one posting id.</summary>
    Task<IReadOnlyList<JobPosting>> ListPublicOpenAsync(Guid tenantId, Guid excludePostingId, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public career site's posting list for one tenant (S-10, Faz 9 gün açılışı TALIMAT'ı): Open status AND not
    /// past its <see cref="JobPosting.ClosingDate"/> (null = no closing date, never expires on its own).
    /// </summary>
    Task<IReadOnlyList<JobPosting>> ListPublicByTenantAsync(Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default);
}

/// <summary>Candidate aggregate repository.</summary>
public interface ICandidateRepository
{
    void Add(Candidate candidate);

    Task<Result<Candidate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candidate>> ListAsync(string? search = null, CancellationToken cancellationToken = default);

    /// <summary>Exact e-mail match within the current tenant scope (career-site apply: reuse an existing candidate instead of duplicating).</summary>
    Task<Candidate?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Not-yet-anonymized candidates created before <paramref name="createdBefore"/> (KVKK retention sweep, see
    /// <see cref="Sense.Modules.Recruitment.Domain.RecruitmentLimits.CandidateDataRetentionMonths"/>). Excludes a
    /// candidate with at least one <see cref="Applications.ApplicationStage.Hired"/> application (G-9-09, Faz 9 gün
    /// açılışı TALIMAT'ı: işe alınmış bir adayın kaydı otomatik saklama süpürmesiyle silinmez - istihdam ilişkisinin
    /// kaynağı olarak elde tutulur; yalnızca manuel/isteğe bağlı imha hâlâ mümkündür).
    /// </summary>
    Task<IReadOnlyList<Candidate>> ListRetentionExpiredAsync(DateTime createdBefore, CancellationToken cancellationToken = default);
}

/// <summary>JobApplication aggregate repository.</summary>
public interface IJobApplicationRepository
{
    void Add(JobApplication application);

    Task<Result<JobApplication>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);

    /// <summary>True if this candidate already has an application for this posting (unique index backs this, but the career-site apply flow wants a friendly error, not a DB exception).</summary>
    Task<bool> ExistsAsync(Guid jobPostingId, Guid candidateId, CancellationToken cancellationToken = default);
}

/// <summary>JobRequisition aggregate repository.</summary>
public interface IJobRequisitionRepository
{
    void Add(JobRequisition requisition);

    Task<Result<JobRequisition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Null-safe variant for the ApprovalCompleted/ApprovalRejected handlers (SubjectId is the requisition's own id).</summary>
    Task<JobRequisition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobRequisition>> ListAsync(RequisitionStatus? status = null, CancellationToken cancellationToken = default);
}

/// <summary>Tag aggregate repository.</summary>
public interface ITagRepository
{
    void Add(Tag tag);

    Task<Result<Tag>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> NameTakenAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
}

/// <summary>RecruitmentFormTemplate aggregate repository.</summary>
public interface IRecruitmentFormTemplateRepository
{
    void Add(RecruitmentFormTemplate template);

    Task<Result<RecruitmentFormTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecruitmentFormTemplate>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
}

/// <summary>RecruitmentFormResponse aggregate repository.</summary>
public interface IRecruitmentFormResponseRepository
{
    void Add(RecruitmentFormResponse response);

    Task<IReadOnlyList<RecruitmentFormResponse>> ListByJobApplicationAsync(Guid jobApplicationId, CancellationToken cancellationToken = default);

    /// <summary>True if the application has at least one submitted response against this template (consumed by AutomationRule gating).</summary>
    Task<bool> ExistsForTemplateAsync(Guid jobApplicationId, Guid templateId, CancellationToken cancellationToken = default);
}

/// <summary>AutomationRule aggregate repository.</summary>
public interface IAutomationRuleRepository
{
    void Add(AutomationRule rule);

    Task<Result<AutomationRule>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutomationRule>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default);

    /// <summary>Active rules matching this exact stage transition (consumed when moving a JobApplication's stage).</summary>
    Task<IReadOnlyList<AutomationRule>> FindActiveForTransitionAsync(ApplicationStage fromStage, ApplicationStage toStage, CancellationToken cancellationToken = default);
}
