using Microsoft.EntityFrameworkCore;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.AutomationRules;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Sense.Modules.Recruitment.Domain.Tags;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>EF Core-backed JobPosting repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class JobPostingRepository(RecruitmentDbContext context) : IJobPostingRepository
{
    public void Add(JobPosting posting) => context.JobPostings.Add(posting);

    public async Task<Result<JobPosting>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var posting = await context.JobPostings.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return posting is null ? Error.NotFound(RecruitmentDomainErrors.PostingNotFound) : posting;
    }

    public async Task<IReadOnlyList<JobPosting>> ListAsync(JobPostingStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = context.JobPostings.AsQueryable();
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        return await query.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed Candidate repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class CandidateRepository(RecruitmentDbContext context) : ICandidateRepository
{
    public void Add(Candidate candidate) => context.Candidates.Add(candidate);

    public async Task<Result<Candidate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var candidate = await context.Candidates.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return candidate is null ? Error.NotFound(RecruitmentDomainErrors.CandidateNotFound) : candidate;
    }

    public async Task<IReadOnlyList<Candidate>> ListAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var query = context.Candidates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.FirstName, term) || EF.Functions.ILike(c.LastName, term) || EF.Functions.ILike(c.Email, term));
        }

        return await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed JobApplication repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class JobApplicationRepository(RecruitmentDbContext context) : IJobApplicationRepository
{
    public void Add(JobApplication application) => context.JobApplications.Add(application);

    public async Task<Result<JobApplication>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = await context.JobApplications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return application is null ? Error.NotFound(RecruitmentDomainErrors.ApplicationNotFound) : application;
    }

    public async Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, CancellationToken cancellationToken = default) =>
        await context.JobApplications
            .Where(a => a.JobPostingId == jobPostingId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
        await context.JobApplications
            .Where(a => a.CandidateId == candidateId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(cancellationToken);
}

/// <summary>EF Core-backed JobRequisition repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class JobRequisitionRepository(RecruitmentDbContext context) : IJobRequisitionRepository
{
    public void Add(JobRequisition requisition) => context.JobRequisitions.Add(requisition);

    public async Task<Result<JobRequisition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var requisition = await context.JobRequisitions.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return requisition is null ? Error.NotFound(RecruitmentDomainErrors.RequisitionNotFound) : requisition;
    }

    public async Task<JobRequisition?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.JobRequisitions.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<JobRequisition>> ListAsync(RequisitionStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = context.JobRequisitions.AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed Tag repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class TagRepository(RecruitmentDbContext context) : ITagRepository
{
    public void Add(Tag tag) => context.Tags.Add(tag);

    public async Task<Result<Tag>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tag = await context.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return tag is null ? Error.NotFound(RecruitmentDomainErrors.TagNotFound) : tag;
    }

    public async Task<bool> NameTakenAsync(Guid tenantId, string name, CancellationToken cancellationToken = default) =>
        await context.Tags.AnyAsync(t => t.Name == name.Trim(), cancellationToken);

    public async Task<IReadOnlyList<Tag>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = context.Tags.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed RecruitmentFormTemplate repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class RecruitmentFormTemplateRepository(RecruitmentDbContext context) : IRecruitmentFormTemplateRepository
{
    public void Add(RecruitmentFormTemplate template) => context.FormTemplates.Add(template);

    public async Task<Result<RecruitmentFormTemplate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await context.FormTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? Error.NotFound(RecruitmentDomainErrors.FormTemplateNotFound) : template;
    }

    public async Task<IReadOnlyList<RecruitmentFormTemplate>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = context.FormTemplates.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed RecruitmentFormResponse repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class RecruitmentFormResponseRepository(RecruitmentDbContext context) : IRecruitmentFormResponseRepository
{
    public void Add(RecruitmentFormResponse response) => context.FormResponses.Add(response);

    public async Task<IReadOnlyList<RecruitmentFormResponse>> ListByJobApplicationAsync(Guid jobApplicationId, CancellationToken cancellationToken = default) =>
        await context.FormResponses
            .Where(r => r.JobApplicationId == jobApplicationId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsForTemplateAsync(Guid jobApplicationId, Guid templateId, CancellationToken cancellationToken = default) =>
        await context.FormResponses.AnyAsync(r => r.JobApplicationId == jobApplicationId && r.TemplateId == templateId, cancellationToken);
}

/// <summary>EF Core-backed AutomationRule repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class AutomationRuleRepository(RecruitmentDbContext context) : IAutomationRuleRepository
{
    public void Add(AutomationRule rule) => context.AutomationRules.Add(rule);

    public async Task<Result<AutomationRule>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return rule is null ? Error.NotFound(RecruitmentDomainErrors.AutomationRuleNotFound) : rule;
    }

    public async Task<IReadOnlyList<AutomationRule>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = context.AutomationRules.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationRule>> FindActiveForTransitionAsync(ApplicationStage fromStage, ApplicationStage toStage, CancellationToken cancellationToken = default) =>
        await context.AutomationRules
            .Where(r => r.IsActive && r.FromStage == fromStage && r.ToStage == toStage)
            .ToListAsync(cancellationToken);
}
