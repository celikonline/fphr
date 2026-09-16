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
}

/// <summary>Candidate aggregate repository.</summary>
public interface ICandidateRepository
{
    void Add(Candidate candidate);

    Task<Result<Candidate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candidate>> ListAsync(string? search = null, CancellationToken cancellationToken = default);
}

/// <summary>JobApplication aggregate repository.</summary>
public interface IJobApplicationRepository
{
    void Add(JobApplication application);

    Task<Result<JobApplication>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);
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
