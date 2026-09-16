using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.JobPostings;

/// <summary>Kolay İK "İlan durumları": Draft/Open/OnHold/Closed are reversible among each other; Archived is terminal.</summary>
public enum JobPostingStatus
{
    Draft,
    Open,
    OnHold,
    Closed,
    Archived,
}

/// <summary>Same value set as Employee.Domain's EmploymentType (roadmap #34) - kept local until Organization.Contracts exposes IEmploymentTypeLookup (R1).</summary>
public enum RecruitmentEmploymentType
{
    FullTime,
    PartTime,
    Temporary,
    Contract,
    Freelance,
    Intern,
    Mobile,
    Apprentice,
    Seasonal,
    AgencyWorker,
    Subcontractor,
}

/// <summary>Why a posting was archived (roadmap #90 status set; a per-tenant customizable catalog is deferred past R0).</summary>
public enum JobPostingArchiveReason
{
    PositionFilled,
    PositionCancelled,
    Other,
}

/// <summary>
/// A job posting (Kolay İK "İlan"). R0 scope: manual lifecycle (draft/publish/close/reopen/archive) and an access list;
/// requisition+approval gating (roadmap #87), the career-site public apply flow, form builder and automation rules are
/// deferred to later Recruitment packages (see docs/analysis/kolayik-video/faz6-durum-takibi.md).
/// </summary>
public sealed class JobPosting : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<JobPostingAccess> _access = [];

    private JobPosting()
    {
    }

    private JobPosting(
        Guid id, Guid tenantId, string title, Guid orgUnitId, Guid? locationId, RecruitmentEmploymentType employmentType,
        string? description, Guid ownerUserId, Guid? hiringManagerUserId, string applySlug, Guid? requisitionId)
        : base(id, tenantId)
    {
        Title = title;
        OrgUnitId = orgUnitId;
        LocationId = locationId;
        EmploymentType = employmentType;
        Description = description;
        OwnerUserId = ownerUserId;
        HiringManagerUserId = hiringManagerUserId;
        ApplySlug = applySlug;
        RequisitionId = requisitionId;
        Status = JobPostingStatus.Draft;
    }

    public string Title { get; private set; } = string.Empty;

    public Guid OrgUnitId { get; private set; }

    public Guid? LocationId { get; private set; }

    public RecruitmentEmploymentType EmploymentType { get; private set; }

    public string? Description { get; private set; }

    public string? CompanyNotes { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid? HiringManagerUserId { get; private set; }

    /// <summary>Optional requisition gating <see cref="Publish"/> (R1, roadmap #87): set, it must be Approved before the posting can go live.</summary>
    public Guid? RequisitionId { get; private set; }

    public JobPostingStatus Status { get; private set; }

    /// <summary>Permanent, never regenerated once assigned - the career-site apply URL is built from it (R3).</summary>
    public string ApplySlug { get; private set; } = string.Empty;

    public DateOnly? ClosingDate { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public DateTime? ArchivedAt { get; private set; }

    public JobPostingArchiveReason? ArchiveReason { get; private set; }

    public IReadOnlyList<JobPostingAccess> Access => _access.AsReadOnly();

    public static Result<JobPosting> Create(
        Guid tenantId, string title, Guid orgUnitId, Guid? locationId, RecruitmentEmploymentType employmentType,
        string? description, Guid ownerUserId, Guid? hiringManagerUserId, Guid? requisitionId = null, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(RecruitmentDomainErrors.PostingTitleRequired);
        }

        var slug = Guid.CreateVersion7().ToString("N")[..RecruitmentLimits.ApplySlugLength];
        return new JobPosting(
            id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(title.Trim(), RecruitmentLimits.TitleMaxLength),
            orgUnitId, locationId, employmentType, description, ownerUserId, hiringManagerUserId, slug, requisitionId);
    }

    public Result Update(
        string title, Guid orgUnitId, Guid? locationId, RecruitmentEmploymentType employmentType,
        string? description, string? companyNotes, Guid? hiringManagerUserId, DateOnly? closingDate)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(RecruitmentDomainErrors.PostingTitleRequired);
        }

        Title = Guard.MaxLength(title.Trim(), RecruitmentLimits.TitleMaxLength);
        OrgUnitId = orgUnitId;
        LocationId = locationId;
        EmploymentType = employmentType;
        Description = description;
        CompanyNotes = companyNotes is null ? null : Guard.MaxLength(companyNotes, RecruitmentLimits.CompanyNotesMaxLength);
        HiringManagerUserId = hiringManagerUserId;
        ClosingDate = closingDate;
        return Result.Success();
    }

    /// <summary>Draft/OnHold/Closed -> Open. "Yayınla" ≠ "kaydet" (roadmap iş kuralı #3).</summary>
    public Result Publish()
    {
        if (Status is not (JobPostingStatus.Draft or JobPostingStatus.OnHold or JobPostingStatus.Closed))
        {
            return Error.Rule(RecruitmentDomainErrors.PostingNotOpenable);
        }

        var wasNeverPublished = PublishedAt is null;
        Status = JobPostingStatus.Open;
        if (wasNeverPublished)
        {
            PublishedAt = DateTime.UtcNow;
        }

        return Result.Success();
    }

    /// <summary>Open -> Closed. Reversible via <see cref="Publish"/> (roadmap: Closed↔Open reversible).</summary>
    public Result Close()
    {
        if (Status != JobPostingStatus.Open)
        {
            return Error.Rule(RecruitmentDomainErrors.PostingNotCloseable);
        }

        Status = JobPostingStatus.Closed;
        return Result.Success();
    }

    /// <summary>Archived is terminal - no reopening (roadmap: "Archive = end of process").</summary>
    public Result Archive(JobPostingArchiveReason reason)
    {
        if (Status == JobPostingStatus.Archived)
        {
            return Error.Rule(RecruitmentDomainErrors.PostingAlreadyArchived);
        }

        Status = JobPostingStatus.Archived;
        ArchiveReason = reason;
        ArchivedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public void SetAccess(IReadOnlyCollection<Guid> userIds)
    {
        _access.Clear();
        foreach (var userId in userIds.Distinct())
        {
            _access.Add(new JobPostingAccess(Id, userId));
        }
    }
}

/// <summary>
/// One user granted explicit access to a posting (roadmap #83 `JobPosting.AccessUserIds[]`), beyond the owner/hiring
/// manager. Owned collection (see ApprovalPolicyStep precedent) - tenant isolation comes from the owning JobPosting.
/// </summary>
public sealed class JobPostingAccess : AuditableRecord
{
    private JobPostingAccess()
    {
    }

    public JobPostingAccess(Guid jobPostingId, Guid userId)
    {
        JobPostingId = jobPostingId;
        UserId = userId;
    }

    public Guid JobPostingId { get; private set; }

    public Guid UserId { get; private set; }
}
