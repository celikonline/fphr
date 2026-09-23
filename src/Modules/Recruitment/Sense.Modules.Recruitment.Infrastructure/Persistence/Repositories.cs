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
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Infrastructure.Persistence;

/// <summary>Clamps API-facing list caps to the shared page-size ceiling.</summary>
internal static class ListTake
{
    public static IQueryable<T> Apply<T>(IQueryable<T> query, int take) =>
        query.Take(Math.Clamp(take, 1, PagingDefaults.MaxPageSize));
}

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

    public async Task<IReadOnlyList<JobPosting>> ListAsync(JobPostingStatus? status, int take, CancellationToken cancellationToken = default)
    {
        var query = context.JobPostings.AsQueryable();
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        return await ListTake.Apply(query.OrderByDescending(p => p.CreatedAt), take).ToListAsync(cancellationToken);
    }

    public async Task<Result<JobPosting>> GetPublicByApplySlugAsync(string applySlug, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var posting = await context.JobPostings
            .IgnoreQueryFilters([RecruitmentDbContext.TenantFilter])
            .FirstOrDefaultAsync(p => p.ApplySlug == applySlug && p.Status == JobPostingStatus.Open
                && (p.ClosingDate == null || p.ClosingDate >= asOf), cancellationToken);
        return posting is null ? Error.NotFound(RecruitmentDomainErrors.PostingNotFound) : posting;
    }

    public async Task<IReadOnlyList<JobPosting>> ListPublicOpenAsync(Guid tenantId, Guid excludePostingId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await context.JobPostings
            .IgnoreQueryFilters([RecruitmentDbContext.TenantFilter])
            .Where(p => p.TenantId == tenantId && p.Status == JobPostingStatus.Open && p.Id != excludePostingId
                && (p.ClosingDate == null || p.ClosingDate >= asOf))
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobPosting>> ListPublicOpenAsync(Guid tenantId, Guid excludePostingId, DateOnly asOf, int take, CancellationToken cancellationToken = default) =>
        await ListTake.Apply(context.JobPostings
            .IgnoreQueryFilters([RecruitmentDbContext.TenantFilter])
            .Where(p => p.TenantId == tenantId && p.Status == JobPostingStatus.Open && p.Id != excludePostingId
                && (p.ClosingDate == null || p.ClosingDate >= asOf))
            .OrderByDescending(p => p.PublishedAt), take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobPosting>> ListPublicByTenantAsync(Guid tenantId, DateOnly asOf, CancellationToken cancellationToken = default) =>
        await context.JobPostings
            .IgnoreQueryFilters([RecruitmentDbContext.TenantFilter])
            .Where(p => p.TenantId == tenantId && p.Status == JobPostingStatus.Open
                && (p.ClosingDate == null || p.ClosingDate >= asOf))
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobPosting>> ListPublicByTenantAsync(Guid tenantId, DateOnly asOf, int take, CancellationToken cancellationToken = default) =>
        await ListTake.Apply(context.JobPostings
            .IgnoreQueryFilters([RecruitmentDbContext.TenantFilter])
            .Where(p => p.TenantId == tenantId && p.Status == JobPostingStatus.Open
                && (p.ClosingDate == null || p.ClosingDate >= asOf))
            .OrderByDescending(p => p.PublishedAt), take)
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<Candidate>> ListAsync(string? search, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Candidates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.FirstName, term) || EF.Functions.ILike(c.LastName, term) || EF.Functions.ILike(c.Email, term));
        }

        return await ListTake.Apply(query.OrderByDescending(c => c.CreatedAt), take).ToListAsync(cancellationToken);
    }

    public async Task<Candidate?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        await FindByNormalizedEmailAsync(Candidate.NormalizeEmail(email), cancellationToken);

    // lower(column) so rows saved before e-mails were normalized (mixed case) still match; the argument is already invariant-lowered (tr-TR safe).
    private async Task<Candidate?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await context.Candidates.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail, cancellationToken);

    public async Task<IReadOnlyList<Candidate>> ListRetentionExpiredAsync(DateTime createdBefore, CancellationToken cancellationToken = default) =>
        await context.Candidates
            .Where(c => c.CreatedAt < createdBefore && !c.Email.EndsWith(Candidate.AnonymizedEmailDomain)
                // G-9-09: işe alınmış (Hired) bir başvurusu olan aday otomatik saklama süpürmesinden hariç tutulur.
                && !context.JobApplications.Any(a => a.CandidateId == c.Id && a.Stage == ApplicationStage.Hired))
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, int take, CancellationToken cancellationToken = default) =>
        await ListTake.Apply(context.JobApplications
            .Where(a => a.JobPostingId == jobPostingId)
            .OrderByDescending(a => a.AppliedAt), take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
        await context.JobApplications
            .Where(a => a.CandidateId == candidateId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, int take, CancellationToken cancellationToken = default) =>
        await ListTake.Apply(context.JobApplications
            .Where(a => a.CandidateId == candidateId)
            .OrderByDescending(a => a.AppliedAt), take)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(Guid jobPostingId, Guid candidateId, CancellationToken cancellationToken = default) =>
        await context.JobApplications.AnyAsync(a => a.JobPostingId == jobPostingId && a.CandidateId == candidateId, cancellationToken);
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

    public async Task<IReadOnlyList<JobRequisition>> ListAsync(RequisitionStatus? status, int take, CancellationToken cancellationToken = default)
    {
        var query = context.JobRequisitions.AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        return await ListTake.Apply(query.OrderByDescending(r => r.CreatedAt), take).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed RequisitionFieldTemplate repository (roadmap #87). One template per (tenant, OrgUnit), tenant isolation via ModuleDbContext's global query filter.</summary>
public sealed class RequisitionFieldTemplateRepository(RecruitmentDbContext context) : IRequisitionFieldTemplateRepository
{
    public void Add(RequisitionFieldTemplate template) => context.RequisitionFieldTemplates.Add(template);

    public async Task<RequisitionFieldTemplate?> FindByOrgUnitAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.RequisitionFieldTemplates.FirstOrDefaultAsync(t => t.OrgUnitId == orgUnitId, cancellationToken);
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

    public async Task<IReadOnlyList<Tag>> ListAsync(bool activeOnly, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Tags.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await ListTake.Apply(query.OrderBy(t => t.Name), take).ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<RecruitmentFormTemplate>> ListAsync(bool activeOnly, int take, CancellationToken cancellationToken = default)
    {
        var query = context.FormTemplates.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await ListTake.Apply(query.OrderBy(t => t.Name), take).ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<RecruitmentFormResponse>> ListByJobApplicationAsync(Guid jobApplicationId, int take, CancellationToken cancellationToken = default) =>
        await ListTake.Apply(context.FormResponses
            .Where(r => r.JobApplicationId == jobApplicationId)
            .OrderByDescending(r => r.SubmittedAt), take)
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

        return await query.OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationRule>> ListAsync(bool activeOnly, int take, CancellationToken cancellationToken = default)
    {
        var query = context.AutomationRules.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(r => r.IsActive);
        }

        return await ListTake.Apply(query.OrderBy(r => r.CreatedAt), take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationRule>> FindActiveForTransitionAsync(ApplicationStage fromStage, ApplicationStage toStage, CancellationToken cancellationToken = default) =>
        await context.AutomationRules
            .Where(r => r.IsActive && r.FromStage == fromStage && r.ToStage == toStage)
            .ToListAsync(cancellationToken);
}
